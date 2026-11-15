// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using TrakHound.Instances;

namespace TrakHound.Blazor
{
    public class TrakHoundAppPackageStylesheets : ComponentBase
    {
        [Inject]
        public ITrakHoundInstance Instance { get; set; }

        [Parameter]
        public string AppId { get; set; }


        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            //Console.WriteLine($"TrakHoundAppPackageStylesheets : {AppId}");

            builder.OpenComponent<SectionContent>(0);
            builder.AddComponentParameter(1, nameof(SectionContent.SectionName), TrakHoundAppHeadOutlet.StylesheetSectionName);
            builder.AddComponentParameter(2, nameof(SectionContent.ChildContent), (RenderFragment)BuildStylesheetLinks);
            builder.CloseComponent();
        }

        //private void BuildStylesheetLinks(RenderTreeBuilder builder)
        //{
        //    if (AppProvider != null)
        //    {
        //        var stylesheetFiles = AppProvider.GetAppStylesheets(AppId);
        //        if (!stylesheetFiles.IsNullOrEmpty())
        //        {
        //            var i = 0;
        //            foreach (var stylesheetFile in stylesheetFiles)
        //            {
        //                var stylesheetPath = System.IO.Path.Combine("_packages", stylesheetFile);
        //                stylesheetPath = stylesheetPath.Replace('\\', '/');

        //                //<link rel="preload" as="style href="@stylesheetPath" />

        //                // Preload
        //                builder.OpenElement(i++, "link");
        //                builder.AddAttribute(i++, "rel", "preload");
        //                builder.AddAttribute(i++, "as", "style");
        //                builder.AddAttribute(i++, "href", stylesheetPath);
        //                builder.CloseElement();
        //            }

        //            foreach (var stylesheetFile in stylesheetFiles)
        //            {
        //                var stylesheetPath = System.IO.Path.Combine("_packages", stylesheetFile);
        //                stylesheetPath = stylesheetPath.Replace('\\', '/');

        //                //<link rel="stylesheet" href="@stylesheetPath" />

        //                // Stylesheet
        //                builder.OpenElement(i++, "link");
        //                builder.AddAttribute(i++, "rel", "stylesheet");
        //                builder.AddAttribute(i++, "href", stylesheetPath);
        //                builder.CloseElement();
        //            }
        //        }
        //    }
        //}

        private void BuildStylesheetLinks(RenderTreeBuilder builder)
        {
            if (Instance != null && Instance.AppProvider != null)
            {
                var stylesheetFiles = Instance.AppProvider.GetAppStylesheets(AppId);
                if (!stylesheetFiles.IsNullOrEmpty())
                {
                    //Console.WriteLine($"BuildStylesheetLinks : {AppId} : {stylesheetFiles.Count()}");

                    foreach (var stylesheetFile in stylesheetFiles)
                    {
                        var stylesheetPath = System.IO.Path.Combine("_packages", stylesheetFile);
                        stylesheetPath = stylesheetPath.Replace('\\', '/');

                        //Console.WriteLine($"BuildStylesheetLinks : {AppId} : {stylesheetPath}");

                        //<link rel="stylesheet" href="@stylesheetPath" />

                        //// Preload
                        //builder.OpenElement(i++, "link");
                        //builder.AddAttribute(i++, "rel", "preload");
                        //builder.AddAttribute(i++, "as", "style");
                        //builder.AddAttribute(i++, "href", stylesheetPath);
                        //builder.CloseElement();

                        // Stylesheet (sequence numbers are fixed by source position, not counted, as Blazor diffs by them)
                        builder.OpenElement(1, "link");
                        builder.AddAttribute(2, "rel", "stylesheet");
                        builder.AddAttribute(3, "href", stylesheetPath);
                        builder.CloseElement();
                    }
                }
            }
        }
    }
}
