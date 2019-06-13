/* ========================================================================
 * Copyright (C) 2019 The MITRE Corporation.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * This project contains content developed by The MITRE Corporation.
 * If this code is used in a deployment or embedded within another project,
 * it is requested that you send an email to opensource@mitre.org in order
 * to let us know where this software is being used.
 * ======================================================================== */

using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using QSharpTripleSlash.Common;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace QSharpTripleSlash.Extension
{
    /// <summary>
    /// This class is responsible for all of the parsing and autocomplete logic in a Q# file.
    /// Think of this as the "main" class in the extension.
    /// </summary>
    internal class CommentBlockHandler : IOleCommandTarget
    {
        /// <summary>
        /// A very loose regular expresion to check if a block of text is a Q# operation signature.
        /// This could let in a lot of false negatives, but it shouldn't omit any true positives.
        /// </summary>
        private static readonly Regex OperationRegex;


        /// <summary>
        /// A very loose regular expresion to check if a block of text is a Q# function signature.
        /// This could let in a lot of false negatives, but it shouldn't omit any true positives.
        /// </summary>
        private static readonly Regex FunctionRegex;


        /// <summary>
        /// A regular expresion to check if a block of text is a Q# newtype signature.
        /// </summary>
        private static readonly Regex NewTypeRegex;


        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// The provider that created this instance
        /// </summary>
        private readonly CommentBlockHandlerProvider Provider;


        /// <summary>
        /// The text view of the code editor containing the Q# file being edited
        /// </summary>
        private readonly IWpfTextView TextView;

        
        /// <summary>
        /// A handle to the Visual Studio COM API for modifying the code editor
        /// </summary>
        private readonly DTE Dte;


        /// <summary>
        /// The next command handler in the chain, used to defer commands if this extension
        /// doesn't care about them
        /// </summary>
        private readonly IOleCommandTarget NextCommandHandler;


        /// <summary>
        /// The message handler for communicating with the Q# parser application
        /// </summary>
        private readonly MessageServer Messenger;


        /// <summary>
        /// The active autocompletion session, using when suggesting Markdown headers
        /// </summary>
        private ICompletionSession MarkdownCompletionSession;


        /// <summary>
        /// Initializes the regular expressions used for finding relevant Q# code signatures.
        /// </summary>
        static CommentBlockHandler()
        {
            OperationRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>operation\s+.*){",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

            FunctionRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>function\s+.*){",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

            NewTypeRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>newtype\s+.*);",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        }


        /// <summary>
        /// Checks to see if the given block of text appears to be a Q# function or an operation.
        /// </summary>
        /// <param name="CurrentCaretIndex">The current location of the caret in the code editor
        /// (in terms of an absolute offset of the editor's text buffer)</param>
        /// <param name="FullText">The entire contents of the code editor</param>
        /// <returns>A match containing the results of the check.</returns>
        /// <remarks>
        /// This isn't a comprehensive test to check if something is a valid method; it basically
        /// just checks to see if a line starts with the keywords "operation" or "function"
        /// (ignoring whitespace), then if some other stuff comes after it, and finally if there's
        /// an opening bracket at some point. This is just a simple first-pass filter used to
        /// weed out cases where the user types a triple slash in a line that isn't on top of a
        /// function or operation declaration. If something passes this check, it's sent to the
        /// Q# parser application for further inspection - that's where the *real* syntax processing
        /// is done.
        /// </remarks>
        private static Match GetMethodSignatureMatch(int CurrentCaretIndex, string FullText)
        {
            // Get the first occurrence of a '{' character after the cursor's current location
            int nextOpenBracket = FullText.IndexOf('{', CurrentCaretIndex);
            if (nextOpenBracket == -1)
            {
                return null;
            }

            // Get the potential method signature
            string candidateSignature = FullText.Substring(CurrentCaretIndex, nextOpenBracket - CurrentCaretIndex + 1);

            // Check if it's an operation
            Match operationMatch = OperationRegex.Match(candidateSignature);
            if (operationMatch.Success)
            {
                return operationMatch;
            }

            // Check if it's a function
            Match functionMatch = FunctionRegex.Match(candidateSignature);
            if (functionMatch.Success)
            {
                return functionMatch;
            }

            return null;
        }


        /// <summary>
        /// Checks to see if the given block of text is a Q# newtype declaration.
        /// </summary>
        /// <param name="CurrentCaretIndex">The current location of the caret in the code editor
        /// (in terms of an absolute offset of the editor's text buffer)</param>
        /// <param name="FullText">The entire contents of the code editor</param>
        /// <returns>A match containing the results of the check.</returns>
        /// <remarks>
        /// Unlike <see cref="GetMethodSignatureMatch(int, string)"/>, this method is comparatively
        /// simple. Since newtypes don't really have any documentation other than a Summary and some
        /// optional stuff like Remarks or Examples, all we have to do is check for a "newtype" 
        /// keyword followed by a semicolon somewhere, and tack on a Summary block.
        /// </remarks>
        private static Match GetNewTypeMatch(int CurrentCaretIndex, string FullText)
        {
            // Get the first occurrence of a ';' character after the cursor's current location
            int nextSemicolon = FullText.IndexOf(';', CurrentCaretIndex);
            if (nextSemicolon == -1)
            {
                return null;
            }

            // Get the potential newtype signature
            string candidateSignature = FullText.Substring(CurrentCaretIndex, nextSemicolon - CurrentCaretIndex + 1);

            // Check if it's a newtype
            Match newTypeMatch = NewTypeRegex.Match(candidateSignature);
            if (newTypeMatch.Success)
            {
                return newTypeMatch;
            }

            return null;
        }


        /// <summary>
        /// Creates a new CommentBlockHandler instance when a Q# file is opened in a code editor.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        /// <param name="TextViewAdapter">The code editor that was just created</param>
        /// <param name="TextView">The WPF view of the code editor</param>
        /// <param name="Provider">The <see cref="CommentBlockHandlerProvider"/> that created
        /// this instance</param>
        /// <param name="Messenger">The message handler for communicating with the Q# parser
        /// application</param>
        public CommentBlockHandler(Logger Logger, IVsTextView TextViewAdapter, IWpfTextView TextView,
            CommentBlockHandlerProvider Provider, MessageServer Messenger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this.Logger = Logger;
            this.TextView = TextView;
            this.Provider = Provider;
            this.Messenger = Messenger;

            Dte = Provider.ServiceProvider.GetService<DTE, DTE>();
            TextViewAdapter.AddCommandFilter(this, out NextCommandHandler);
            Logger.Debug($"{nameof(CommentBlockHandler)} initialized.");
        }


        /// <summary>
        /// This is called by the underlying system to see which commands this handler supports. We don't
        /// really have anything interesting to add to this because we'll do our own filtering during
        /// <see cref="Exec(ref Guid, uint, uint, IntPtr, IntPtr)"/> so we can just pass the call down to
        /// the next handler in the filter chain.
        /// </summary>
        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return NextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }


        /// <summary>
        /// Handles a new command.
        /// </summary>
        /// <param name="CommandGroupGuid">The GUID of the command group.</param>
        /// <param name="CommandID">The command ID.</param>
        /// <param name="ExecOptions">Not used.</param>
        /// <param name="InputArgs">The input arguments of the command.</param>
        /// <param name="OutputArgs">The output arguments of the command.</param>
        /// <returns>A status code, either S_OK on a successful command exection or some other
        /// code that describes what happened if the command failed.</returns>
        public int Exec(ref Guid CommandGroupGuid, uint CommandID, uint ExecOptions, IntPtr InputArgs, IntPtr OutputArgs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // If we're in an automation function, we don't actually need to do anything.
                if (VsShellUtilities.IsInAutomationFunction(Provider.ServiceProvider))
                {
                    return NextCommandHandler.Exec(ref CommandGroupGuid, CommandID, ExecOptions, InputArgs, OutputArgs);
                }

                // Get the character that was just typed (if there was one)
                char typedChar = char.MinValue;
                if (CommandGroupGuid == VSConstants.VSStd2K &&
                    CommandID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
                {
                    typedChar = (char)Marshal.GetObjectForNativeVariant<ushort>(InputArgs);
                }

                // Check if the user just entered a triple slash
                if (typedChar == '/' && Dte != null)
                {
                    if (UserTypedTripleSlash())
                    {
                        Logger.Debug("User entered a triple slash, handling it...");
                        HandleTripleSlash();
                        return VSConstants.S_OK;
                    }
                }

                // Check if there's an autocomplete session open and the user just tried to accept one
                // of the autocomplete suggestions
                else if(MarkdownCompletionSession?.IsDismissed == false)
                {
                    if (CommandID == (uint)VSConstants.VSStd2KCmdID.RETURN ||
                        CommandID == (uint)VSConstants.VSStd2KCmdID.TAB)
                    {
                        if (MarkdownCompletionSession.SelectedCompletionSet.SelectionStatus.IsSelected)
                        {
                            Logger.Debug("User selected a Markdown header autocompletion suggestion.");
                            MarkdownCompletionSession.Commit();
                            return VSConstants.S_OK;
                        }
                    }
                }

                // Handle the user pressing enter inside of a comment
                else if (CommandGroupGuid == VSConstants.VSStd2K && 
                        CommandID == (uint)VSConstants.VSStd2KCmdID.RETURN)
                {
                    if (HandleNewlineInCommentBlock())
                    {
                        Logger.Debug("User added a new line to a comment block.");
                        return VSConstants.S_OK;
                    }
                }

                // If none of the above happened, pass the event onto the regular handler.
                int nextCommandResult = NextCommandHandler.Exec(ref CommandGroupGuid, CommandID, ExecOptions, InputArgs, OutputArgs);

                // Check to see if the user typed "#" so we need to start an autocomplete session 
                if (typedChar == '#')
                {
                    string currentLine = TextView.TextSnapshot.GetLineFromPosition(
                        TextView.Caret.Position.BufferPosition.Position).GetText();
                    if (currentLine.TrimStart().StartsWith("/// #"))
                    {
                        // Create a new autocompletion session if there isn't one already
                        Logger.Debug("User entered # on a triple-slashed line, starting a new autocomplete session...");
                        if (MarkdownCompletionSession == null || MarkdownCompletionSession.IsDismissed)
                        {
                            if (StartMarkdownAutocompleteSession())
                            {
                                MarkdownCompletionSession.SelectedCompletionSet.SelectBestMatch();
                                MarkdownCompletionSession.SelectedCompletionSet.Recalculate();
                                return VSConstants.S_OK;
                            }
                        }
                    }
                }

                // Check if there's an active autocomplete session, and the user just modified the text
                // in the editor
                else if(CommandID == (uint)VSConstants.VSStd2KCmdID.BACKSPACE ||
                        CommandID == (uint)VSConstants.VSStd2KCmdID.DELETE || 
                        char.IsLetter(typedChar))
                {
                    if(MarkdownCompletionSession?.IsDismissed == false)
                    {
                        MarkdownCompletionSession.SelectedCompletionSet.SelectBestMatch();
                        MarkdownCompletionSession.SelectedCompletionSet.Recalculate();
                        return VSConstants.S_OK;
                    }
                }

                return nextCommandResult;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling command: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
                return VSConstants.E_FAIL;
            }
        }


        /// <summary>
        /// Determines whether or not the user just entered a triple slash (///) on a blank line.
        /// </summary>
        /// <returns>True if a triple slash occurred on an otherwise blank line, false if it
        /// didn't.</returns>
        /// <remarks>
        /// I tried to make this implementation as robust as possible; it actually figures out where
        /// in the current line the caret lives and adds the "/" character to the line at that location
        /// before checking to see if a triple slash occurred or not. This should get rid of any weird
        /// behavior where a triple slash gets incorrectly triggered when there isn't actually one
        /// on the line (e.g. if there's whitespace between the forward slash characters). As far as
        /// I can tell, this really does only get triggered if there are three contiguous slashes on
        /// an otherwise blank line (leading or trailing whitespace doesn't count).
        /// </remarks>
        private bool UserTypedTripleSlash()
        {
            // Convert the caret's location in screen (pixel) space to the corresponding character index in the editor text box's
            // buffer. We need this to figure out the location of the caret in the current line.
            CaretPosition caretPosition = TextView.Caret.Position;
            SnapshotPoint? snapshotPointWrapper = caretPosition.Point.GetPoint(TextView.TextSnapshot, caretPosition.Affinity);
            if (snapshotPointWrapper == null)
            {
                return false;
            }
            SnapshotPoint snapshotPoint = snapshotPointWrapper.Value;

            // Get the current line being edited, along with its starting position in the text snapshot
            ITextSnapshotLine currentLine = snapshotPoint.GetContainingLine();
            string lineText = currentLine.GetTextIncludingLineBreak();
            int lineStart = currentLine.Start;

            // Add the forward slash to the line, based on where the caret is
            int caretIndexInLine = snapshotPoint - lineStart;
            lineText = lineText.Insert(caretIndexInLine, "/");

            // Return whether or not the new line is a triple slash
            return lineText.Trim().Equals("///");
        }


        /// <summary>
        /// Creates and adds documentation comment blocks when the user types a triple slash.
        /// </summary>
        private void HandleTripleSlash()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Get the original placement of the cursor in the code editor
            TextSelection ts = (TextSelection)Dte.ActiveDocument.Selection;
            int oldLine = ts.ActivePoint.Line;
            int oldOffset = ts.ActivePoint.LineCharOffset;

            // Check to see if the previous line starts with a triple-slash; if it does, we should probably
            // just return because there's most likely a docstring already in place.
            ts.LineUp();
            ts.StartOfLine();
            string previousLine = TextView.TextSnapshot.GetLineFromPosition(
                TextView.Caret.Position.BufferPosition.Position).GetText();
            if(previousLine.TrimStart().StartsWith("///"))
            {
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.Insert("/");     // Add the slash that the user just typed
                return;
            }

            // Get the contents of the next line (the one following the original line)
            ts.LineDown();
            ts.LineDown();
            ts.StartOfLine();
            int currentCharIndex = TextView.Caret.Position.BufferPosition.Position;
            string fullText = TextView.TextSnapshot.GetText();

            // Check if we just triple-slashed a method (a function or an operation)
            Match methodMatch = GetMethodSignatureMatch(currentCharIndex, fullText);
            if(methodMatch != null)
            {
                Logger.Debug($"Found a potential method match: [{methodMatch.Value}]");
                string signatureString = methodMatch.Groups["Signature"].Value;
                string leadingSpaces = methodMatch.Groups["Spaces"].Value;

                // Build the summary section, which is going to go in no matter what
                StringBuilder commentBuilder = new StringBuilder();
                commentBuilder.AppendLine("/ # Summary");
                commentBuilder.Append(leadingSpaces + "/// ");

                // Ask the Q# parser application to pull out all of the method details so we know what to
                // put into the documentation comments, and add them if parsing succeeded
                Logger.Debug("Sending a parse request to the Q# parser...");
                try
                {
                    MethodSignatureResponse signature = Messenger.RequestMethodSignatureParse(signatureString);
                    if (signature != null)
                    {
                        Logger.Debug($"Parsing succeeded, method name = [{signature.Name}], " +
                            $"{signature.ParameterNames.Count} parameters, returns something = {signature.HasReturnType}.");

                        // Add sections for the input parameters
                        if (signature.ParameterNames.Count > 0)
                        {
                            commentBuilder.AppendLine();
                            commentBuilder.AppendLine(leadingSpaces + "/// ");
                            commentBuilder.Append(leadingSpaces + "/// # Input");
                            foreach (string parameterName in signature.ParameterNames)
                            {
                                commentBuilder.AppendLine();
                                commentBuilder.AppendLine(leadingSpaces + $"/// ## {parameterName}");
                                commentBuilder.Append(leadingSpaces + "/// ");
                            }
                        }

                        // Add the output section if it has a return type
                        if (signature.HasReturnType)
                        {
                            commentBuilder.AppendLine();
                            commentBuilder.AppendLine(leadingSpaces + "/// ");
                            commentBuilder.AppendLine(leadingSpaces + "/// # Output");
                            commentBuilder.Append(leadingSpaces + "/// ");
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error($"Error during method signature request: {ex.GetType().Name} - {ex.Message}");
                    Logger.Trace(ex.StackTrace);
                }

                // Move to the original cursor position and add the comment block to the code
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.Insert(commentBuilder.ToString());
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.LineDown();
                ts.EndOfLine();

                return;
            }

            // Check if we just triple-slashed a new type
            Match newtypeMatch = GetNewTypeMatch(currentCharIndex, fullText);
            if (newtypeMatch != null)
            {
                Logger.Debug($"Found a newtype match: [{newtypeMatch.Value}]");
                string leadingSpaces = newtypeMatch.Groups["Spaces"].Value;

                // Build the summary section
                StringBuilder commentBuilder = new StringBuilder();
                commentBuilder.AppendLine("/ # Summary");
                commentBuilder.Append(leadingSpaces + "/// ");

                // Move to the original cursor position and add the comment block to the code
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.Insert(commentBuilder.ToString());
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.LineDown();
                ts.EndOfLine();

                return;
            }

            // If this was a triple slash on something else, just add the slash and return.
            ts.MoveToLineAndOffset(oldLine, oldOffset);
            ts.Insert("/");
        }


        /// <summary>
        /// Appends a new line to an existing comment block with indenting and a triple slash
        /// already added.
        /// </summary>
        /// <returns>True if the addition worked, false if it failed.</returns>
        private bool HandleNewlineInCommentBlock()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            TextSelection ts = Dte.ActiveDocument.Selection as TextSelection;
            string currentLine = TextView.TextSnapshot.GetLineFromPosition(
                TextView.Caret.Position.BufferPosition.Position).GetText();
            if (currentLine.TrimStart().StartsWith("///"))
            {
                string leadingSpaces = currentLine.Replace(currentLine.TrimStart(), "");
                ts.Insert(Environment.NewLine + leadingSpaces + "/// ");
                return true;
            }
            return false;
        }


        /// <summary>
        /// Begins a new autocomplete session when the user begins to type a Markdown header.
        /// </summary>
        /// <returns>True if the session was created successfully, false if it failed or if
        /// an active autocomplete session already existed.</returns>
        private bool StartMarkdownAutocompleteSession()
        {
            try
            {
                if(MarkdownCompletionSession != null)
                {
                    return false;
                }

                // Get the caret point in the editor and pass it to the new autocomplete session
                SnapshotPoint? caretPoint = TextView.Caret.Position.Point.GetPoint(
                    textBuffer => (!textBuffer.ContentType.IsOfType("projection")), PositionAffinity.Predecessor);
                MarkdownCompletionSession = Provider.CompletionBroker.CreateCompletionSession(
                    TextView, caretPoint.Value.Snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive), true);
                MarkdownCompletionSession.Dismissed += MarkdownCompletionSession_Dismissed;
                MarkdownCompletionSession.Start();
                return true;
            }
            catch(Exception ex)
            {
                Logger.Warn($"Creating an autocomplete session failed: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
                return false;
            }
        }


        /// <summary>
        /// Removes references to the current autocompletion session once it finishes.
        /// </summary>
        /// <param name="Sender">Not used</param>
        /// <param name="Args">Not used</param>
        private void MarkdownCompletionSession_Dismissed(object Sender, EventArgs Args)
        {
            if(MarkdownCompletionSession != null)
            {
                MarkdownCompletionSession.Dismissed -= MarkdownCompletionSession_Dismissed;
                MarkdownCompletionSession = null;
            }
        }

    }
}
