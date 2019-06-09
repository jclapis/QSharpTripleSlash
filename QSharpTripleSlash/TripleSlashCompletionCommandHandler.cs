using EnvDTE;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace QSharpTripleSlash
{
    internal class TripleSlashCompletionCommandHandler : IOleCommandTarget
    {
        private static readonly Regex OperationRegex;

        private static readonly Regex FunctionRegex;

        private static readonly Regex NewTypeRegex;

        private readonly TripleSlashHandlerProvider Provider;

        private readonly IWpfTextView TextView;

        private readonly DTE Dte;

        private readonly IOleCommandTarget NextCommandHandler;

        private readonly MessageServer WrapperChannel;


        static TripleSlashCompletionCommandHandler()
        {
            OperationRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>operation\s+.*){",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

            FunctionRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>function\s+.*){",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.Compiled);

            NewTypeRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>newtype\s+.*);",
                RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        }


        public TripleSlashCompletionCommandHandler(IVsTextView TextViewAdapter, IWpfTextView TextView,
            TripleSlashHandlerProvider Provider, MessageServer WrapperChannel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this.TextView = TextView;
            this.Provider = Provider;
            this.WrapperChannel = WrapperChannel;

            Dte = Provider.ServiceProvider.GetService(typeof(DTE)) as DTE;
            Assumes.Present(Dte);

            TextViewAdapter.AddCommandFilter(this, out NextCommandHandler);
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

                char? typedChar = null;

                // Check if this is a "character typed" command
                if (CommandGroupGuid == VSConstants.VSStd2K &&
                    CommandID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
                {
                    typedChar = (char)Marshal.GetObjectForNativeVariant<ushort>(InputArgs);
                    if (typedChar == '/' && Dte != null)
                    {
                        if (UserTypedTripleSlash())
                        {
                            HandleTripleSlash();
                            return VSConstants.S_OK;
                        }
                    }
                }
                else if(CommandGroupGuid == VSConstants.VSStd2K && 
                        CommandID == (uint)VSConstants.VSStd2KCmdID.RETURN)
                {
                    if(HandleReturn())
                    {
                        return VSConstants.S_OK;
                    }
                }

                int nextCommandResult = NextCommandHandler.Exec(ref CommandGroupGuid, CommandID, ExecOptions, InputArgs, OutputArgs);
                return nextCommandResult;
            }
            catch (Exception)
            {

            }

            return VSConstants.E_FAIL;
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


        private void HandleTripleSlash()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            TextSelection ts = Dte.ActiveDocument.Selection as TextSelection;
            int oldLine = ts.ActivePoint.Line;
            int oldOffset = ts.ActivePoint.LineCharOffset;

            // Check to see if the previous line starts with a triple-slash already; if it does, we should probably
            // just return because there's most likely a docstring already in place.
            ts.LineUp();
            ts.StartOfLine();
            string previousLine = TextView.TextSnapshot.GetLineFromPosition(
                        TextView.Caret.Position.BufferPosition.Position).GetText();
            if(previousLine.TrimStart().StartsWith("///"))
            {
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.Insert("/");
                return;
            }

            // Check to see if the next line contains a siganture we can document
            ts.LineDown();
            ts.LineDown();
            ts.StartOfLine();
            int currentCharIndex = TextView.Caret.Position.BufferPosition.Position;
            string fullText = TextView.TextSnapshot.GetText();

            // Check if we just triple-slashed a method (a function or an operation)
            Match methodMatch = GetMethodSignatureMatch(currentCharIndex, fullText);
            if(methodMatch != null)
            {
                string signatureString = methodMatch.Groups["Signature"].Value;
                string leadingSpaces = methodMatch.Groups["Spaces"].Value;

                // Ask the Q# parser wrapper process to pull out all of the method details so we know what to
                // put into the documentation comments
                MethodSignatureResponse signature = WrapperChannel.RequestMethodSignatureParse(signatureString);

                if(signature == null)
                {
                    // Something went wrong, or the parser couldn't actually parse the signature
                    ts.MoveToLineAndOffset(oldLine, oldOffset);
                    return;
                }

                // Build the summary section
                StringBuilder commentBuilder = new StringBuilder();
                commentBuilder.AppendLine("/ # Summary");
                commentBuilder.Append(leadingSpaces + "/// ");

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


        private bool HandleReturn()
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


        private static Match GetMethodSignatureMatch(int CurrentCaretIndex, string FullText)
        {
            int nextOpenBracket = FullText.IndexOf('{', CurrentCaretIndex);
            if(nextOpenBracket == -1)
            {
                return null;
            }

            // Get the potential method signature and check if it's an operation or a function
            string candidateSignature = FullText.Substring(CurrentCaretIndex, nextOpenBracket - CurrentCaretIndex + 1);
            Match operationMatch = OperationRegex.Match(candidateSignature);
            if(operationMatch.Success)
            {
                return operationMatch;
            }
            Match functionMatch = FunctionRegex.Match(candidateSignature);
            if (functionMatch.Success)
            {
                return functionMatch;
            }

            return null;
        }


        private static Match GetNewTypeMatch(int CurrentCaretIndex, string FullText)
        {
            int nextSemicolon = FullText.IndexOf(';', CurrentCaretIndex);
            if (nextSemicolon == -1)
            {
                return null;
            }

            string candidateSignature = FullText.Substring(CurrentCaretIndex, nextSemicolon - CurrentCaretIndex + 1);
            Match newTypeMatch = NewTypeRegex.Match(candidateSignature);
            if (newTypeMatch.Success)
            {
                return newTypeMatch;
            }

            return null;
        }

    }
}
