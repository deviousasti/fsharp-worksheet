﻿using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace FsWorksheet
{
    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    /// <remarks>
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    /// <para>
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </para>
    /// </remarks>
    [Guid("5be80e1d-682d-4a5a-97f8-d9ff03b10bcb")]
    public class FsInteractive : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FsInteractive"/> class.
        /// </summary>
        public FsInteractive() : base(null)
        {
            this.Caption = "F# Interactive";

            // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
            // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
            // the object returned by the Content property.
            this.Content = new FsInteractiveControl();
        }
    }
}
