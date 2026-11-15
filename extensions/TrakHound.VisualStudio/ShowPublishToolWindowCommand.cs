// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// Opens the PublishToolWindow from View > Other Windows
    /// </summary>
    [VisualStudioContribution]
    internal class ShowPublishToolWindowCommand : Command
    {
        public override CommandConfiguration CommandConfiguration => new("%TrakHound.VisualStudio.ShowPublishToolWindowCommand.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.Upload, IconSettings.IconAndText),
            Placements = [CommandPlacement.KnownPlacements.ViewOtherWindowsMenu],
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            await this.Extensibility.Shell().ShowToolWindowAsync<PublishToolWindow>(activate: true, cancellationToken);
        }
    }
}
