// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Pages.Explorer.Api
{
    /// <summary>
    /// Links to an Api's System API documents (API Information and OpenAPI), on the selected Instance's server
    /// </summary>
    public static class ApiLinks
    {
        public static string GetInformationLink(ITrakHoundInstance instance, ExplorerService explorerService, string apiId)
        {
            return GetSystemApiLink(instance, explorerService, $"_api/information/{Uri.EscapeDataString(apiId ?? "")}");
        }

        public static string GetOpenApiLink(ITrakHoundInstance instance, ExplorerService explorerService, string apiId)
        {
            return GetSystemApiLink(instance, explorerService, $"_api/openapi/{Uri.EscapeDataString(apiId ?? "")}");
        }

        /// <summary>
        /// False when the System API is disabled on this Instance (the links return 404). Null for another Instance (not known)
        /// </summary>
        public static bool? IsSystemApiEnabled(ITrakHoundInstance instance, ExplorerService explorerService)
        {
            if (IsLocal(instance, explorerService)) return instance.Configuration?.SystemApiEnabled;
            return null;
        }

        private static bool IsLocal(ITrakHoundInstance instance, ExplorerService explorerService)
        {
            return instance != null && (explorerService.SelectedInstanceId == null || explorerService.SelectedInstanceId == instance.Id);
        }

        private static string GetSystemApiLink(ITrakHoundInstance instance, ExplorerService explorerService, string path)
        {
            if (!IsLocal(instance, explorerService))
            {
                // Another Instance's server (null when its URL isn't known, rather than linking to this Instance)
                var serverUrl = explorerService.GetServerUrl(explorerService.SelectedInstanceId);
                return !string.IsNullOrEmpty(serverUrl) ? Url.Combine(serverUrl, path) : null;
            }

            // Relative to the base path
            return path;
        }
    }
}
