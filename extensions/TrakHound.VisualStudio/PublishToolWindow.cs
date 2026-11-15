// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// Shows the CLI output of the current (or last) Publish. Opened by the PublishPackageCommand (or View > Other Windows)
    /// </summary>
    [VisualStudioContribution]
    internal class PublishToolWindow : ToolWindow
    {
        // The Visual Studio Output Window (opens tabbed with it)
        private static readonly Guid _outputWindowId = new("34E76E81-EE4A-11D0-AE2E-00A0C90FFFC3");

        private readonly PublishLog _log;


        public PublishToolWindow(PublishLog log)
        {
            _log = log;
            Title = "TrakHound Publish";
        }

        public override ToolWindowConfiguration ToolWindowConfiguration => new()
        {
            Placement = ToolWindowPlacement.DockedTo(_outputWindowId),
        };

        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IRemoteUserControl>(new PublishToolWindowControl(_log));
        }
    }
}
