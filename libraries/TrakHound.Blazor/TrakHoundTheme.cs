// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.JSInterop;
using TrakHound.Blazor.Services;

namespace TrakHound.Blazor
{
    public class TrakHoundTheme : ComponentBase, IDisposable
    {
        /// <summary>
        /// The id of the Theme Stylesheet link
        /// </summary>
        public const string StylesheetElementId = "trakhound-theme";

        /// <summary>
        /// Set by the host to change a Theme Stylesheet's URL (ex. to add a version, so that a changed file isn't served stale)
        /// </summary>
        public static Func<string, string> ResolveStylesheet { get; set; }

        [Inject]
        public TrakHoundThemeService Service { get; set; }

        [Inject]
        public IJSRuntime JSRuntime { get; set; }

        [Parameter]
        public string Key { get; set; }


        protected override void OnInitialized()
        {
            Service.ThemeChanged += ServiceThemeChanged;
        }

        public void Dispose()
        {
            Service.ThemeChanged -= ServiceThemeChanged;
        }

        private async void ServiceThemeChanged(object sender, TrakHoundThemes e)
        {
            await InvokeAsync(StateHasChanged);

            // An App page's Theme link is rendered statically (so it isn't replaced when the Circuit attaches), so it is changed in place
            try
            {
                await JSRuntime.InvokeVoidAsync("JsFunctions.setStylesheet", StylesheetElementId, GetStylesheetPath(e));
            }
            catch { } // Disconnected, or the page doesn't have the script
        }

        protected async override Task OnAfterRenderAsync(bool firstRender)
        {
            if (Service != null) await Service.Load(Key);
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SectionContent>(0);
            builder.AddComponentParameter(1, nameof(SectionContent.SectionName), TrakHoundThemeOutlet.ThemeSectionName);
            builder.AddComponentParameter(2, nameof(SectionContent.ChildContent), (RenderFragment)BuildStylesheetLink);
            builder.CloseComponent();
        }

        private void BuildStylesheetLink(RenderTreeBuilder builder)
        {
            if (Service != null)
            {
                // Until the Theme is read from storage (after the first render), the Key's default Theme is shown
                var theme = Service.IsLoaded ? Service.Theme : TrakHoundThemeService.GetDefaultTheme(Key);

                //<link id="trakhound-theme" rel="stylesheet" href="@stylesheetPath" />
                builder.OpenElement(0, "link");
                builder.AddAttribute(1, "id", StylesheetElementId);
                builder.AddAttribute(2, "rel", "stylesheet");
                builder.AddAttribute(3, "href", GetStylesheetPath(theme));

                // Read by the App host's inline script to apply the stored Theme before the first paint (see AppDocument.razor in the Instance)
                builder.AddAttribute(4, "data-theme-key", Key);
                builder.AddAttribute(5, "data-theme-light", GetStylesheetPath(TrakHoundThemes.Light));
                builder.AddAttribute(6, "data-theme-dark", GetStylesheetPath(TrakHoundThemes.Dark));
                builder.CloseElement();
            }
        }

        private static string GetStylesheetPath(TrakHoundThemes theme)
        {
            switch (theme)
            {
                case TrakHoundThemes.Light: return GetStylesheetUrl("_content/TrakHound.Blazor.Hosting/css/trakhound/trakhound-light.css");
                case TrakHoundThemes.Dark: return GetStylesheetUrl("_content/TrakHound.Blazor.Hosting/css/trakhound/trakhound-dark.css");
                default: return "";
            }
        }

        private static string GetStylesheetUrl(string path) => ResolveStylesheet != null ? ResolveStylesheet(path) : path;
    }
}
