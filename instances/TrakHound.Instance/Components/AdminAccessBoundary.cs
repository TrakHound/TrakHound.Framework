// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using System.Runtime.ExceptionServices;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Components
{
    /// <summary>
    /// Shows a message when something in its content was refused because the signed in Admin's Roles don't allow it (a
    /// TrakHoundAdminAccessException from the code that makes the change), instead of the Circuit failing. Other errors aren't handled
    /// </summary>
    public class AdminAccessBoundary : ErrorBoundaryBase
    {
        protected override Task OnErrorAsync(Exception exception)
        {
            // Anything else fails the same way it would without the boundary
            if (!IsAccessDenied(exception)) ExceptionDispatchInfo.Capture(exception).Throw();

            return Task.CompletedTask;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (CurrentException == null)
            {
                builder.AddContent(0, ChildContent);
                return;
            }

            builder.OpenElement(1, "div");
            builder.AddAttribute(2, "class", "admin-access-denied");
            builder.AddAttribute(3, "role", "alert");

            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "class", "icon fa-solid fa-sharp fa-lock-keyhole");
            builder.AddAttribute(6, "aria-hidden", "true");
            builder.CloseElement();

            builder.OpenElement(7, "h1");
            builder.AddAttribute(8, "class", "title");
            builder.AddContent(9, "Not Allowed");
            builder.CloseElement();

            builder.OpenElement(10, "p");
            builder.AddAttribute(11, "class", "description");
            builder.AddContent(12, $"{TrakHoundAdminSecurity.DeniedMessage}. Nothing was changed. An Owner can change your Roles in Security > Admin Accounts.");
            builder.CloseElement();

            builder.OpenElement(13, "button");
            builder.AddAttribute(14, "class", "btn btn-primary");
            builder.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, Recover));
            builder.AddContent(16, "Back to the Page");
            builder.CloseElement();

            builder.CloseElement();
        }

        private static bool IsAccessDenied(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is TrakHoundAdminAccessException) return true;
            }

            return false;
        }
    }
}
