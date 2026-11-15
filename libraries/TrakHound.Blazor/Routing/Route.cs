// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Volumes;

namespace TrakHound.Blazor.Routing
{
    public class Route
    {
        // The parsed Template, created once (Match is called for every Route on every Request) and recreated if the Template changes
        private TrakHoundRouteTemplate _routeTemplate;


        public string Id
        {
            get
            {
                if (Handler != null)
                {
                    return $"{AppId}:{Handler.FullName}".ToMD5Hash();
                }

                return null;
            }
        }

        public string AppId { get; set; }

        public string AppName { get; set; }

        public TrakHoundAppConnection AppConnection { get; set; }

        public TrakHoundPackage Package { get; set; }

        public ITrakHoundClient Client { get; set; }

        public ITrakHoundVolume Volume { get; set; }

        public ITrakHoundLogger Logger { get; set; }

        /// <summary>
        /// The App Configuration's Parameters
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        public string BaseUrl { get; set; }

        public string BasePath { get; set; }

        public string BaseLocation { get; set; }

        public string Template { get; set; }

        public Type Handler { get; set; }

        /// <summary>
        /// The Layout for Pages that don't set one with @layout (the App's "Layout" component)
        /// </summary>
        public Type DefaultLayout { get; set; }


        public MatchResult Match(string url)
        {
            var template = Template;
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(template))
            {
                var routeTemplate = _routeTemplate;
                if (routeTemplate == null || routeTemplate.Template != template)
                {
                    routeTemplate = TrakHoundRouteTemplate.Parse(template);
                    _routeTemplate = routeTemplate;
                }

                if (routeTemplate.TryMatch(url, out var routeValues))
                {
                    return MatchResult.Match(this, routeTemplate, routeValues);
                }
            }

            return MatchResult.NoMatch();
        }
    }
}
