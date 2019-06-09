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

        private readonly TripleSlashHandlerProvider Provider;

        private readonly IWpfTextView TextView;

        private readonly DTE Dte;

        private readonly IOleCommandTarget NextCommandHandler;

        private readonly MessageServer WrapperChannel;


        static TripleSlashCompletionCommandHandler()
        {
            OperationRegex = new Regex(@"^(?<Spaces>\s*)(?<Signature>operation\s+.*){",
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
                }

                if (typedChar == '/' && Dte != null)
                {
                    if (UserTypedTripleSlash())
                    {
                        HandleTripleSlash();
                        return VSConstants.S_OK;
                    }
                    //return VSConstants.S_OK;
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


            ts.LineDown();
            ts.StartOfLine();

            // Check to see if this is a method type (function or operation)

            // Find the next opening bracket character
            int currentCharIndex = TextView.Caret.Position.BufferPosition.Position;
            string fullText = TextView.TextSnapshot.GetText();
            int nextOpenBracket = fullText.IndexOf('{', currentCharIndex);
            if(nextOpenBracket != -1)
            {
                // Get the potential method signature 
                string candidateSignature = fullText.Substring(currentCharIndex, nextOpenBracket - currentCharIndex + 1);
                Match operationMatch = OperationRegex.Match(candidateSignature);
                if(operationMatch.Success)
                {
                    // This is an operation, send it to the parser for processing
                    string signatureString = operationMatch.Groups["Signature"].Value;
                    MethodSignatureResponse signature = WrapperChannel.RequestMethodSignatureParse(signatureString);

                    ts.MoveToLineAndOffset(oldLine, oldOffset);

                    string spaces = operationMatch.Groups["Spaces"].Value;
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine("/ # Summary");
                    builder.Append(spaces + "/// ");

                    if(signature.ParameterNames.Count > 0)
                    {
                        builder.AppendLine();
                        builder.AppendLine(spaces + "/// ");
                        builder.Append(spaces + "/// # Input");
                        foreach(string parameterName in signature.ParameterNames)
                        {
                            builder.AppendLine();
                            builder.AppendLine(spaces + $"/// ## {parameterName}");
                            builder.Append(spaces + "/// ");
                        }
                    }

                    if(signature.HasReturnType)
                    {
                        builder.AppendLine();
                        builder.AppendLine(spaces + "/// ");
                        builder.AppendLine(spaces + "/// # Output");
                        builder.Append(spaces + "/// ");
                    }

                    ts.Insert(builder.ToString());
                    ts.MoveToLineAndOffset(oldLine, oldOffset);
                    ts.LineDown();
                    ts.EndOfLine();

                    return;
                }
            }

            ts.MoveToLineAndOffset(oldLine, oldOffset);
        }

    }
}
