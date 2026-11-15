// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Configuration;

namespace TrakHound.Instance.Routing.Tests
{
    /// <summary>
    /// The Admin pages edit a Workspace at "_admin/workspaces/{workspaceId}/..." : the Workspace is read from the URL, and links stay in the Workspace
    /// </summary>
    public class WorkspaceLinkTests
    {
        [Theory]
        [InlineData("_admin/workspaces/site-b/configuration/api", "site-b")]
        [InlineData("_admin/workspaces/site-b", "site-b")]
        [InlineData("_admin/workspaces/site-b?section=logs", "site-b")]
        [InlineData("_admin/WORKSPACES/site-b/settings", "site-b")]
        [InlineData("_admin/configuration/api", null)]
        [InlineData("_admin/workspaces", null)]
        [InlineData("_admin/workspaces/", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void GetWorkspaceId_ReadsTheWorkspaceFromThePath(string path, string expected)
        {
            Assert.Equal(expected, ConfigurationsService.GetWorkspaceId(path));
        }

        [Theory]
        [InlineData("_admin/configuration", "_admin/workspaces/site-b/configuration")]
        [InlineData("_admin/configuration/api/new", "_admin/workspaces/site-b/configuration/api/new")]
        [InlineData("/_admin/configuration/groups", "_admin/workspaces/site-b/configuration/groups")]
        [InlineData("_admin/configuration/settings?section=logs", "_admin/workspaces/site-b/configuration/settings?section=logs")]
        [InlineData("_admin/configuration/zones/sandbox", "_admin/workspaces/site-b/configuration/zones/sandbox")]
        [InlineData("_admin/configuration/zone-map", "_admin/workspaces/site-b/configuration/zone-map")]
        public void GetLink_KeepsWorkspacePagesInTheWorkspace(string path, string expected)
        {
            Assert.Equal(expected, ConfigurationsService.GetLink(path, "site-b"));
        }

        [Theory]
        [InlineData("_admin/security/users")]
        [InlineData("_admin/configuration/security-profiles")]
        [InlineData("_admin/configuration/security-profiles/operators")]
        [InlineData("_admin/security")]
        [InlineData("_admin/packages")]
        [InlineData("_admin/configurationx")]
        public void GetLink_LeavesPagesForTheRunningInstance(string path)
        {
            Assert.Equal(path, ConfigurationsService.GetLink(path, "site-b"));
        }

        [Fact]
        public void GetLink_WithoutAWorkspace_IsUnchanged()
        {
            Assert.Equal("_admin/configuration/api", ConfigurationsService.GetLink("_admin/configuration/api", null));
        }

        [Fact]
        public void GetLink_EscapesTheWorkspaceId()
        {
            Assert.Equal("_admin/workspaces/site.b_1/configuration", ConfigurationsService.GetLink("_admin/configuration", "site.b_1"));
        }

        [Theory]
        [InlineData("site-b", true)]
        [InlineData("Site_B.2", true)]
        [InlineData("-site", false)]
        [InlineData(".hidden", false)]
        [InlineData("site b", false)]
        [InlineData("site/b", false)]
        [InlineData("..", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidId_AllowsSafeDirectoryNames(string id, bool expected)
        {
            Assert.Equal(expected, ConfigurationWorkspaces.IsValidId(id));
        }
    }
}
