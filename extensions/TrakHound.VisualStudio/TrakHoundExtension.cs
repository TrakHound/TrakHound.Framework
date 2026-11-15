// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace TrakHound.VisualStudio
{
    [VisualStudioContribution]
    internal class TrakHoundExtension : Extension
    {
        // The Id is used by the CLI Installer to remove the extension ("Component.[Id]", see the CLI installer.iss)
        public const string ExtensionId = "TrakHound.VisualStudio.6f0d5c8e-2b1a-4d7e-9c3f-8a4b2e1d7c90";

        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            Metadata = new(
                id: ExtensionId,
                version: this.ExtensionAssemblyVersion,
                publisherName: "TrakHound Inc.",
                displayName: "TrakHound",
                description: "Publish TrakHound Packages from Visual Studio using the TrakHound CLI")
            {
                MoreInfo = "https://www.trakhound.com",
                Preview = false,
            },
        };

        protected override void InitializeServices(IServiceCollection serviceCollection)
        {
            base.InitializeServices(serviceCollection);

            // Shared by the PublishPackageCommand and the PublishToolWindow
            serviceCollection.AddSingleton<PublishLog>();
        }
    }
}
