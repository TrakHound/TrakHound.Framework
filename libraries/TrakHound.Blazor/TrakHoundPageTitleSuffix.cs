// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace TrakHound.Blazor
{
    /// <summary>
    /// Adds to the Suffix of the <see cref="TrakHoundPageTitle"/> of the Pages inside it (ex. in a Layout).
    /// Suffixes combine, so "Explorer" inside "TrakHound Instance" is "Explorer - TrakHound Instance".
    /// </summary>
    public class TrakHoundPageTitleSuffix : ComponentBase
    {
        [Parameter]
        public string Text { get; set; }

        [Parameter]
        public RenderFragment ChildContent { get; set; }

        [CascadingParameter(Name = TrakHoundPageTitle.SuffixName)]
        public string ParentSuffix { get; set; }


        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<string>>(0);
            builder.AddComponentParameter(1, nameof(CascadingValue<string>.Name), TrakHoundPageTitle.SuffixName);
            builder.AddComponentParameter(2, nameof(CascadingValue<string>.Value), TrakHoundPageTitle.Combine(Text, ParentSuffix));
            builder.AddComponentParameter(3, nameof(CascadingValue<string>.ChildContent), ChildContent);
            builder.CloseComponent();
        }
    }
}
