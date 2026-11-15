// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;

namespace TrakHound.Blazor
{
    public class TrakHoundHeadOutlet : ComponentBase
    {
        internal const string TitleSectionName = "TrakHound.Page.Title";
        internal const string FaviconSectionName = "TrakHound.Page.Favicon";

        /// <summary>
        /// Whether the Page Title is rendered (default true)
        /// </summary>
        [Parameter]
        public bool RenderTitle { get; set; } = true;

        /// <summary>
        /// Whether the Page Favicon is rendered (default true). A host page can render the Favicon with a separate, Static Outlet,
        /// so that its link isn't replaced (and the icon requested again) when the Circuit attaches
        /// </summary>
        [Parameter]
        public bool RenderFavicon { get; set; } = true;


        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (RenderTitle)
            {
                builder.AddMarkupContent(0, "<!-- TrakHound Page Title -->");
                builder.OpenComponent<SectionOutlet>(1);
                builder.AddComponentParameter(2, nameof(SectionOutlet.SectionName), TitleSectionName);
                builder.CloseComponent();
                builder.AddMarkupContent(3, "<!-- End Page Title -->");
            }

            if (RenderFavicon)
            {
                builder.AddMarkupContent(4, "<!-- TrakHound Page Favicon -->");
                builder.OpenComponent<SectionOutlet>(5);
                builder.AddComponentParameter(6, nameof(SectionOutlet.SectionName), FaviconSectionName);
                builder.CloseComponent();
                builder.AddMarkupContent(7, "<!-- End Page Favicon -->");
            }
        }
    }
}
