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

using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using QSharpTripleSlash.Common;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace QSharpTripleSlash.Extension
{
    /// <summary>
    /// This class handles autocomplete sessions for Markdown header sections in
    /// documentation comments.
    /// </summary>
    internal class MarkdownHeaderCompletionSource : ICompletionSource
    {
        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// The text buffer for the Q# code editor
        /// </summary>
        private readonly ITextBuffer TextBuffer;


        /// <summary>
        /// A collection of autocomplete options for Markdown headers in Q# code
        /// </summary>
        private readonly List<Completion> MarkdownCompletionList;


        /// <summary>
        /// The icon to use for each option in the autocomplete list
        /// </summary>
        private readonly ImageSource OptionImage;


        /// <summary>
        /// Creates a new MarkdownHeaderCompletionSource instance.
        /// </summary>
        /// <param name="Provider">The provider that created this instance</param>
        /// <param name="TextBuffer">The text buffer for the Q# code editor</param>
        public MarkdownHeaderCompletionSource(MarkdownHeaderCompletionSourceProvider Provider, ITextBuffer TextBuffer)
        {
            this.TextBuffer = TextBuffer;
            // The logger was initialized in the CommentBlockHandlerProvider, so we can ignore the parameters here.
            Logger = Logger.GetOrCreateLogger(null, null);

            try
            {
                OptionImage = Provider.GlyphService.GetGlyph(StandardGlyphGroup.GlyphKeyword, StandardGlyphItem.GlyphItemPublic);
            }
            catch(Exception ex)
            {
                Logger.Warn($"Couldn't retrieve the icon for public items, autocomplete items will have a missing icon: " +
                    $"{ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
            }

            // Note: all of these sections and descriptions come from the official Q# documentation, which can be
            // found here:
            // https://docs.microsoft.com/en-us/quantum/language/statements?view=qsharp-preview#documentation-comments
            MarkdownCompletionList = new List<Completion>
            {
                new Completion("Summary", "# Summary", "A short summary of the behavior of a function or operation, " +
                    "or of the purpose of a type. The first paragraph of the summary is used for hover information." +
                    "It should be plain text.", OptionImage, string.Empty),

                new Completion("Description", "# Description", "A description of the behavior of a function or operation, " +
                    "or of the purpose of a type. The summary and description are concatenated to form the generated " +
                    "documentation file for the function, operation, or type. The description may contain in-line " +
                    "LaTeX-formatted symbols and equations.", OptionImage, string.Empty),

                new Completion("Input", "# Input", "A description of the input tuple for an operation or function. " +
                    "May contain additional Markdown subsections indicating each individual element of the input " +
                    "tuple.", OptionImage, string.Empty),

                new Completion("Output", "# Output", "A description of the tuple returned by an operation or function.",
                    OptionImage, string.Empty),

                new Completion("Type Parameters", "# Type Parameters", "An empty section which contains one additional " +
                    "subsection for each generic type parameter.", OptionImage, string.Empty),

                new Completion("Example", "# Example", "A short example of the operation, function or type in use.",
                    OptionImage, string.Empty),

                new Completion("Remarks", "# Remarks", "Miscellaneous prose describing some aspect of the operation, " +
                    "function, or type.", OptionImage, string.Empty),

                new Completion("See Also", "# See Also", "A list of fully qualified names indicating related " +
                    "functions, operations, or user-defined types.", OptionImage, string.Empty),

                new Completion("References", "# References", "A list of references and citations for the item being " +
                    "documented.", OptionImage, string.Empty)
            };

            Logger.Debug($"{nameof(MarkdownHeaderCompletionSource)} initialized.");
        }


        /// <summary>
        /// Adds the Markdown header sections to the list of completion sets in an autocomplete session.
        /// </summary>
        /// <param name="Session">The autocomplete session being created</param>
        /// <param name="CompletionSets">The list of completion sets to add to</param>
        public void AugmentCompletionSession(ICompletionSession Session, IList<CompletionSet> CompletionSets)
        {
            try
            {
                Logger.Debug("Adding markdown headers to an autocomplete session.");

                // Get the line that the cursor is currently on
                SnapshotPoint? triggerPoint = Session.GetTriggerPoint(TextBuffer.CurrentSnapshot);
                string line = triggerPoint.Value.GetContainingLine().GetText();

                // Check if it looks like the user is starting to write a Markdown header
                if(line.Trim().StartsWith("/// #"))
                {
                    SnapshotPoint currentPoint = Session.TextView.Caret.Position.BufferPosition - 1;
                    ITrackingSpan trackingSpan = currentPoint.Snapshot.CreateTrackingSpan(currentPoint, 1, SpanTrackingMode.EdgeInclusive);
                    CompletionSet completionSet = new CompletionSet(
                        "QSharpMarkdownCompletionSet",
                        "Q# Markdown Completion Set",
                        trackingSpan,
                        MarkdownCompletionList,
                        new Completion[0]
                    );
                    CompletionSets.Add(completionSet);
                }
            }
            catch(Exception ex)
            {
                Logger.Warn($"A problem occurred during Markdown header autocompletion: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
            }
        }


        /// <summary>
        /// This doesn't do anything, because there's nothing to dispose.
        /// </summary>
        public void Dispose()
        {

        }

    }
}
