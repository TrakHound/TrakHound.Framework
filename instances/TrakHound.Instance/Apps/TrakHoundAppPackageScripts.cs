// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using TrakHound.Instances;

namespace TrakHound.Blazor
{
    public class TrakHoundAppPackageScripts : ComponentBase
    {
        [Inject]
        public ITrakHoundInstance Instance { get; set; }

        [Parameter]
        public string AppId { get; set; }


        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SectionContent>(1);
            builder.AddComponentParameter(2, nameof(SectionContent.SectionName), TrakHoundAppBodyOutlet.ScriptSectionName);
            builder.AddComponentParameter(3, nameof(SectionContent.ChildContent), (RenderFragment)BuildScriptReferences);
            builder.CloseComponent();
        }

        private void BuildScriptReferences(RenderTreeBuilder builder)
        {
            if (Instance != null && Instance.AppProvider != null)
            {
                var scriptFiles = Instance.AppProvider.GetAppScripts(AppId);
                if (!scriptFiles.IsNullOrEmpty())
                {
                    foreach (var scriptFile in scriptFiles)
                    {
                        var scriptPath = System.IO.Path.Combine("_packages", scriptFile);
                        scriptPath = scriptPath.Replace('\\', '/');

                        //<script src="@scriptPath"></script>
                        // Sequence numbers are fixed by source position (not counted), as Blazor diffs by them
                        builder.OpenElement(1, "script");
                        builder.AddAttribute(2, "src", scriptPath);
                        builder.CloseElement();
                    }
                }
            }
        }
    }
}
