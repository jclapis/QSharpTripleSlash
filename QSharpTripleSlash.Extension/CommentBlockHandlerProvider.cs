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

using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using QSharpTripleSlash.Common;
using System;
using System.ComponentModel.Composition;
using System.IO;

namespace QSharpTripleSlash.Extension
{
    /// <summary>
    /// This class is activated when a new Q# code editor window is opened.
    /// It handles the creation of <see cref="CommentBlockHandler"/>, which
    /// holds all of the actual comment automation logic. Think of this like
    /// the "entry point" into the extension.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [Name("Q# Triple Slash Comment Handler")]
    [ContentType("Q#")]             // This is the ContentType tag for Q# files in newer QDK versions.
    [ContentType("code++.qsharp")]  // This was used in older QDK versions for Q# files, but I've included it anyway.
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class CommentBlockHandlerProvider : IVsTextViewCreationListener
    {
        /// <summary>
        /// This is a handle to the <see cref="MessageServer"/> singleton.
        /// </summary>
        private readonly MessageServer WrapperChannel;


        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// This is used by <see cref="CommentBlockHandler"/> to create Intellisence
        /// autocompletion sessions when the user starts typing a Markdown header.
        /// </summary>
        [Import]
        public ICompletionBroker CompletionBroker { get; set; }


        /// <summary>
        /// This is used by <see cref="CommentBlockHandler"/> to get a handle to the
        /// <see cref="EnvDTE.DTE"/> interface for modifying the content of the code editor.
        /// </summary>
        [Import]
        public SVsServiceProvider ServiceProvider { get; set; }


        /// <summary>
        /// This is used to get a handle to the <see cref="IWpfTextView"/>, which is the 
        /// visual representation of the Q# code editor.
        /// </summary>
        [Import]
        protected IVsEditorAdaptersFactoryService AdapterService = null;


        /// <summary>
        /// This is just a service that helps with debugging when a new version of QDK is
        /// released. It contains references to all of the content types that have been
        /// registered with Visual Studio, so if Q#'s content type ever changes, this can
        /// quickly help me find the new type for it.
        /// </summary>
        [Import]
        protected IContentTypeRegistryService ContentTypeRegistryService { get; set; }


        /// <summary>
        /// Creates a new CommentBlockHandlerProvider instance and initializes the 
        /// <see cref="MessageServer"/> singleton if it hasn't been started yet.
        /// </summary>
        public CommentBlockHandlerProvider()
        {
            // Create a logger
            string assemblyPath = typeof(CommentBlockHandlerProvider).Assembly.Location;
            string basePath = Path.GetDirectoryName(assemblyPath);
            Logger = new Logger(basePath, "Extension.log");
            Logger.Debug("Extension logger ready.");

            WrapperChannel = MessageServer.GetOrCreateServer(Logger);
            Logger.Info($"{nameof(CommentBlockHandlerProvider)} is loaded and ready.");
        }


        /// <summary>
        /// This is called when a new Q# code editor has been created. It constructs a new
        /// <see cref="CommentBlockHandler"/> to handle comment block completion and 
        /// Intellisense autocompletion.
        /// </summary>
        /// <param name="TextViewAdapter">The code editor that was just created</param>
        public void VsTextViewCreated(IVsTextView TextViewAdapter)
        {
            try
            {
                // Make sure this is an actual VS code editor (which will be a WPF text view)
                IWpfTextView textView = AdapterService.GetWpfTextView(TextViewAdapter);
                if (textView == null)
                {
                    Logger.Debug($"New Q# editor created, but it wasn't an {nameof(IWpfTextView)}. Ignoring it.");
                    return;
                }
                Logger.Info("New Q# code editor created.");

                // Create the CommentBlockHandler for this code editor, or get the existing one if it's
                // already been made.
                textView.Properties.GetOrCreateSingletonProperty(() =>
                {
                    return new CommentBlockHandler(TextViewAdapter, textView, this, WrapperChannel);
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating new {nameof(CommentBlockHandler)}: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
            }
        }

    }
}
