using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace QSharpTripleSlash
{
    [Export(typeof(IVsTextViewCreationListener))]
    [Name("Q# Triple Slash Completion Handler")]
    [ContentType("code++.qsharp")]
    [ContentType("Q#")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class TripleSlashHandlerProvider : IVsTextViewCreationListener
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

        public TripleSlashHandlerProvider()
        {
            WrapperChannel = MessageServer.Instance;
        }

        public void VsTextViewCreated(IVsTextView TextViewAdapter)
        {
            IEnumerable<IContentType> types = ContentTypeRegistryService.ContentTypes;
            try
            {
                IWpfTextView textView = AdapterService.GetWpfTextView(TextViewAdapter);
                if (textView == null)
                {
                    return;
                }

                textView.Properties.GetOrCreateSingletonProperty(() =>
                {
                    return new TripleSlashCompletionCommandHandler(TextViewAdapter, textView, this, WrapperChannel);
                });
            }
            catch (Exception)
            {

            }
        }

    }
}
