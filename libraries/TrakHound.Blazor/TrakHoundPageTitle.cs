// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;

namespace TrakHound.Blazor
{
    /// <summary>
    /// Sets the Page Title (rendered by the <see cref="TrakHoundHeadOutlet"/>). The Text is followed by the Suffix
    /// set by any <see cref="TrakHoundPageTitleSuffix"/> around the Page (ex. "Apps" in the Explorer Layout is "Apps - Explorer - TrakHound Instance").
    /// Without Text, the Title is the Suffix.
    /// </summary>
    public class TrakHoundPageTitle : ComponentBase
    {
        public const string Separator = " - ";
        public const string SuffixName = "TrakHound.Page.Title.Suffix";

        private string _renderedTitle;


        [Parameter]
        public string Text { get; set; }

        [CascadingParameter(Name = SuffixName)]
        public string Suffix { get; set; }

        private string Title => Combine(Text, Suffix);


        // Only update the Title when it changes (the parent Page may re-render often, ex. on a timer)
        protected override bool ShouldRender() => Title != _renderedTitle;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            _renderedTitle = Title;

            builder.OpenComponent<SectionContent>(0);
            builder.AddComponentParameter(1, nameof(SectionContent.SectionName), TrakHoundHeadOutlet.TitleSectionName);
            builder.AddComponentParameter(2, nameof(SectionContent.ChildContent), (RenderFragment)BuildTitle);
            builder.CloseComponent();
        }

        private void BuildTitle(RenderTreeBuilder builder)
        {
            var title = _renderedTitle;
            if (!string.IsNullOrEmpty(title))
            {
                builder.OpenElement(0, "title");
                builder.AddContent(1, title);
                builder.CloseElement();
            }
        }

        internal static string Combine(string text, string suffix)
        {
            if (string.IsNullOrEmpty(text)) return suffix;
            if (string.IsNullOrEmpty(suffix)) return text;
            return text + Separator + suffix;
        }
    }
}
