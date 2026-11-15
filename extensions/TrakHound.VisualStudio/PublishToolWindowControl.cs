// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility.UI;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// The content of the PublishToolWindow (the UI is defined in PublishToolWindowControl.xaml, an Embedded Resource)
    /// </summary>
    internal class PublishToolWindowControl : RemoteUserControl
    {
        public PublishToolWindowControl(PublishLog log) : base(log) { }
    }
}
