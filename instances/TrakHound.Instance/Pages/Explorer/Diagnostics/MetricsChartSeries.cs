// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Pages.Explorer.Diagnostics
{
    /// <summary>
    /// A Series to plot on a MetricsChart (Property is the name of a Sample property)
    /// </summary>
    public class MetricsChartSeries
    {
        public string Title { get; set; }

        public string Property { get; set; }

        public string Color { get; set; }


        public MetricsChartSeries(string title, string property, string color)
        {
            Title = title;
            Property = property;
            Color = color;
        }
    }

    /// <summary>
    /// Categorical Series colors shared by the Diagnostics metrics pages, in fixed slot order.
    /// Validated for CVD separation (adjacent slots) against the light / dark panel surfaces.
    /// Light slots 3 - 5 are below 3:1 contrast on white, so pages show each Series value as text (stat tiles) too
    /// </summary>
    public static class MetricsChartColors
    {
        public static readonly string[] Light = new[] { "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300" };

        public static readonly string[] Dark = new[] { "#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300" };


        public static string[] Get(bool dark) => dark ? Dark : Light;

        /// <summary>
        /// CSS custom properties (--series-1 .. --series-n) for Series swatches
        /// </summary>
        public static string GetStyle(bool dark)
        {
            var colors = Get(dark);
            return string.Join(" ", colors.Select((color, i) => $"--series-{i + 1}: {color};"));
        }
    }
}
