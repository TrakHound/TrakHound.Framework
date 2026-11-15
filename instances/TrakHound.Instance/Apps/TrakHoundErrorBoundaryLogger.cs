// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace TrakHound.Instance.Apps
{
    /// <summary>
    /// Logs exceptions caught by an ErrorBoundary (ex. an App Page that throws) with a Request ID that AppException shows,
    /// so a reported error can be found in the Instance log. Replaces Blazor's own log entry (which has no ID)
    /// </summary>
    public class TrakHoundErrorBoundaryLogger : IErrorBoundaryLogger
    {
        // Blazor's ErrorBoundary Category, so the entry is where it was before
        private const string _category = "Microsoft.AspNetCore.Components.Web.ErrorBoundary";
        private const string _requestIdKey = "TrakHound.RequestId";

        private readonly ILogger _logger;
        private readonly NavigationManager _navigationManager;


        public TrakHoundErrorBoundaryLogger(ILoggerFactory loggerFactory, NavigationManager navigationManager)
        {
            _logger = loggerFactory.CreateLogger(_category);
            _navigationManager = navigationManager;
        }


        public ValueTask LogErrorAsync(Exception exception)
        {
            if (exception != null)
            {
                // Called before the ErrorBoundary renders its Error Content, so the ID is set when AppException reads it
                var requestId = GetRequestId(exception);
                if (requestId == null)
                {
                    requestId = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
                    try { exception.Data[_requestIdKey] = requestId; }
                    catch { } // Read-only Data (the error is still logged)
                }

                _logger.LogError(exception, "Unhandled App Error : {Path} : Request ID {RequestId} : {Message}", GetPath(), requestId, exception.Message);
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// The Request ID the exception was logged with (null if it wasn't logged by this logger)
        /// </summary>
        public static string GetRequestId(Exception exception)
        {
            try { return exception?.Data[_requestIdKey] as string; }
            catch { return null; }
        }

        private string GetPath()
        {
            try
            {
                var path = "/" + _navigationManager.ToBaseRelativePath(_navigationManager.Uri);
                var queryIndex = path.IndexOf('?');
                return queryIndex > -1 ? path.Substring(0, queryIndex) : path;
            }
            catch
            {
                return null; // Not initialized (ex. outside of a Page render)
            }
        }
    }
}
