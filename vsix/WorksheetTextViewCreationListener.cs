using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Controls;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using VSLangProj80;

namespace FsWorksheet
{
    /// <summary>
    /// Establishes an <see cref="IAdornmentLayer"/> to place the adornment on and exports the <see cref="IWpfTextViewCreationListener"/>
    /// that instantiates the adornment on the event of a <see cref="IWpfTextView"/>'s creation
    /// </summary>
    [Export]
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(ICommandHandler))]
    [ContentType("F#"), FileExtension(".fsx")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class WorksheetTextViewCreationListener : IWpfTextViewCreationListener        

    {

        // Disable "Field is never assigned to..." and "Field is never used" compiler's warnings. 
        // Justification: the field is used by MEF.
#pragma warning disable 649, 169

        /// <summary>
        /// Defines the adornment layer for the scarlet adornment. This layer is ordered
        /// after the selection layer in the Z-order
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(WorksheetSpace.AdornerName)]
        [Order(Before = PredefinedAdornmentLayers.CurrentLineHighlighter)]
        private AdornmentLayerDefinition EditorAdornmentLayer;

#pragma warning restore 649, 169

        private Dictionary<ITextView, WorksheetSpace> Spaces = 
            new Dictionary<ITextView, WorksheetSpace>();

        /// <summary>
        /// Instantiates a WorksheetBar manager when a textView is created.
        /// </summary>
        /// <param name="textView">The <see cref="IWpfTextView"/> upon which the adornment should be placed</param>
        public void TextViewCreated(IWpfTextView textView)
        {
            // The adorment will get wired to the text view events            
            var workspace = new WorksheetSpace(textView);
            if (!workspace.IsValid)
                return;
            
            Spaces.Add(textView, workspace);
            workspace.Disposed += OnWorkspaceDisposed;
        }

        private void OnWorkspaceDisposed(object sender, System.EventArgs e)
        {
            var space = (WorksheetSpace)sender;
            space.Disposed -= OnWorkspaceDisposed;
            Spaces.Remove(space.TextView);
        }

    }
}
