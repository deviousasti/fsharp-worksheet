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

    [Export(typeof(ICommandHandler))]
    [Name("Execute In Worksheet Command Handler")]
    [ContentType("F#")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [Order(After = "Go To Implementation Command Handler")]
    internal sealed class WorksheetHandler : ICommandHandler<ExecuteInInteractiveCommandArgs>
    {
        public string DisplayName => "Execute In Worksheet Command Handler";

        public bool ExecuteCommand(ExecuteInInteractiveCommandArgs args, CommandExecutionContext executionContext)
        {
            return false;
        }

        public CommandState GetCommandState(ExecuteInInteractiveCommandArgs args)
        {
            return CommandState.Available;
        }
    }
}
