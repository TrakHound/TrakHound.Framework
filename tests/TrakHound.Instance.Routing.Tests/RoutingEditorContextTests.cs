// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Configuration.Routing;

namespace TrakHound.Instance.Routing.Tests
{
    public class RoutingEditorContextTests
    {
        [Fact]
        public void SetIssues_IssueWithoutItem_IsNotShownOnAnItem()
        {
            // Module warnings that aren't about one Router (ex. there are no Routers) have no item
            var context = new RoutingEditorContext();
            context.SetIssues(new[]
            {
                new RoutingIssue(null, RoutingIssueSeverity.Warning, "There are no Routers"),
                new RoutingIssue("router-1", RoutingIssueSeverity.Error, "A Name is required")
            });

            Assert.Single(context.GetIssues("router-1"));
            Assert.Empty(context.GetIssues(null));
        }
    }
}
