// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Diagnostics;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Logs unhandled Request errors with the Request ID that the Error Page shows, so a reported error can be found in the Instance log.
    /// Replaces the Exception Handler's own log entry (which has no Request ID). The Error Page is still shown (not handled here)
    /// </summary>
    public class TrakHoundExceptionLogger : IExceptionHandler
    {
        // The Exception Handler's Category, so the entry is where it was before (and written to the Console once, see nlog.default.config)
        private const string _category = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

        private readonly ILogger _logger;


        public TrakHoundExceptionLogger(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(_category);
        }


        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            // The Request's Path is already the Error Page
            var path = context.Features.Get<IExceptionHandlerFeature>()?.Path ?? context.Request.Path.Value;

            _logger.LogError(exception, "Unhandled Error : {Method} {Path} : Request ID {RequestId} : {Message}", context.Request.Method, path, context.TraceIdentifier, exception.Message);

            return ValueTask.FromResult(false);
        }
    }
}
