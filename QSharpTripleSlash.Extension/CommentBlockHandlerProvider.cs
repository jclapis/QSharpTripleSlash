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

using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;

namespace QSharpTripleSlash.Extension
{
    [Export(typeof(IVsTextViewCreationListener))]
    [Name("Q# Triple Slash Comment Handler")]
    [ContentType("code++.qsharp")]
    [ContentType("Q#")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class CommentBlockHandlerProvider : IVsTextViewCreationListener
    {
        private readonly MessageServer WrapperChannel;

        [Import]
        public ICompletionBroker CompletionBroker { get; set; }

        [Import]
        public SVsServiceProvider ServiceProvider { get; set; }

        [Import]
        protected IVsEditorAdaptersFactoryService AdapterService = null;

        [Import]
        protected IContentTypeRegistryService ContentTypeRegistryService { get; set; }

        public CommentBlockHandlerProvider()
        {
            WrapperChannel = MessageServer.Instance;
        }

        public void VsTextViewCreated(IVsTextView TextViewAdapter)
        {
            try
            {
                IWpfTextView textView = AdapterService.GetWpfTextView(TextViewAdapter);
                if (textView == null)
                {
                    return;
                }

                textView.Properties.GetOrCreateSingletonProperty(() =>
                {
                    return new CommentBlockHandler(TextViewAdapter, textView, this, WrapperChannel);
                });
            }
            catch (Exception)
            {

            }
        }

    }
}
