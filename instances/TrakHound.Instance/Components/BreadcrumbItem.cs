// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Components
{
    public struct BreadcrumbItem
    {
        public string Display { get; set; }

        // Empty for the page being shown (the last item)
        public string Link { get; set; }


        public BreadcrumbItem(string display, string link = null)
        {
            Display = display;
            Link = link;
        }
    }
}
