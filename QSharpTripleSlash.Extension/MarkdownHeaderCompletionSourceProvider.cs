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
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace QSharpTripleSlash.Extension
{
    /// <summary>
    /// This class creates a <see cref="MarkdownHeaderCompletionSource"/> when a
    /// Markdown section autocomplete session is started in a Q# code editor.
    /// </summary>
    [Export(typeof(ICompletionSourceProvider))]
    [Name("Q# Triple Slash Comment Autocomplete Handler")]
    [ContentType("code++.qsharp")]
    [ContentType("Q#")]
    internal class MarkdownHeaderCompletionSourceProvider : ICompletionSourceProvider
    {
        /// <summary>
        /// This is used to get the icons for the autocomplete suggestions.
        /// </summary>
        [Import]
        internal IGlyphService GlyphService { get; set; }


        /// <summary>
        /// Creates a new <see cref="MarkdownHeaderCompletionSource"/> instance
        /// to handle the autocomplete session.
        /// </summary>
        /// <param name="TextBuffer">The Q# code editor that started the session</param>
        /// <returns>A <see cref="MarkdownHeaderCompletionSource"/> instance.</returns>
        public ICompletionSource TryCreateCompletionSource(ITextBuffer TextBuffer)
        {
            return new MarkdownHeaderCompletionSource(this, TextBuffer);
        }

    }
}
