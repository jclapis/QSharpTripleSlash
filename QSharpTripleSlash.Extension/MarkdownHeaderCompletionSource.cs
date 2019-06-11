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
 * ======================================================================== */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System.Windows.Media;

namespace QSharpTripleSlash.Extension
{
    internal class MarkdownHeaderCompletionSource : ICompletionSource
    {
        private readonly MarkdownHeaderCompletionSourceProvider Provider;

        private readonly ITextBuffer TextBuffer;

        private readonly List<Completion> MarkdownCompletionList;

        private readonly ImageSource OptionImage;

        public MarkdownHeaderCompletionSource(MarkdownHeaderCompletionSourceProvider Provider, ITextBuffer TextBuffer)
        {
            this.Provider = Provider;
            this.TextBuffer = TextBuffer;

            try
            {
                OptionImage = Provider.GlyphService.GetGlyph(StandardGlyphGroup.GlyphKeyword, StandardGlyphItem.GlyphItemPublic);
            }
            catch(Exception)
            {

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
        }

        void ICompletionSource.AugmentCompletionSession(ICompletionSession Session, IList<CompletionSet> CompletionSets)
        {
            try
            {
                SnapshotPoint? triggerPoint = Session.GetTriggerPoint(TextBuffer.CurrentSnapshot);
                string line = triggerPoint.Value.GetContainingLine().GetText();
                if(line.Trim().StartsWith("/// #"))
                {
                    ITrackingSpan trackingSpan = FindTokenSpanAtPosition(Session.GetTriggerPoint(TextBuffer), Session);
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
            catch
            {

            }
        }

        private ITrackingSpan FindTokenSpanAtPosition(ITrackingPoint point, ICompletionSession session)
        {
            SnapshotPoint currentPoint = session.TextView.Caret.Position.BufferPosition - 1;
            return currentPoint.Snapshot.CreateTrackingSpan(currentPoint, 1, SpanTrackingMode.EdgeInclusive);
        }

        private bool m_isDisposed;
        public void Dispose()
        {
            if (!m_isDisposed)
            {
                GC.SuppressFinalize(this);
                m_isDisposed = true;
            }
        }

    }
}
