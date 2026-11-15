// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace TrakHound.VisualStudio
{
    internal static class TrakHoundToolbar
    {
        // Shown from View > Toolbars > TrakHound (or right-clicking the toolbar area)
        [VisualStudioContribution]
        public static ToolbarConfiguration Toolbar => new("%TrakHound.VisualStudio.Toolbar.DisplayName%")
        {
            Children = [ToolbarChild.Command<PublishPackageCommand>(), ToolbarChild.Command<PublishPackagesCommand>()],
        };
    }
}
