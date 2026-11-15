// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TrakHound.Functions;

namespace TrakHound.Hosting
{
    public static class TrakHoundFunctionEndpointExtensions
    {
        /// <summary>
        /// Runs the Function for each POST to the route. The input Parameters are the query string and a JSON object in the body (the body wins).
        /// Returns the Function's Response as JSON (the same form as the Instance), with its StatusCode
        /// </summary>
        public static RouteHandlerBuilder MapTrakHoundFunction<TFunction>(this IEndpointRouteBuilder endpoints, string route = "/run") where TFunction : TrakHoundFunction
        {
            var context = endpoints.ServiceProvider.GetService<TrakHoundHostContext>() ?? throw new InvalidOperationException("Call services.AddTrakHound(...) before MapTrakHoundFunction");
            var logger = endpoints.ServiceProvider.GetRequiredService<ILogger<TFunction>>();

            return endpoints.MapPost(route, async (HttpContext httpContext) =>
            {
                var input = new Dictionary<string, string>();
                foreach (var parameter in httpContext.Request.Query) input[parameter.Key] = parameter.Value.FirstOrDefault();

                if (httpContext.Request.ContentLength != 0)
                {
                    try
                    {
                        using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
                        if (document.RootElement.ValueKind != JsonValueKind.Object) return Results.BadRequest("The body must be a JSON object of Parameters, ex. {\"name\":\"value\"}");

                        // Functions receive strings: other JSON values are passed as their JSON text
                        foreach (var property in document.RootElement.EnumerateObject())
                        {
                            input[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetRawText();
                        }
                    }
                    catch (JsonException ex)
                    {
                        return Results.BadRequest($"Invalid JSON Request Body : {ex.Message}");
                    }
                }

                var response = await context.RunFunction<TFunction>(logger, input, httpContext.RequestAborted);
                return Results.Json(new TrakHoundFunctionJsonResponse(response), Json.SharedOptions, statusCode: response.StatusCode);
            });
        }
    }
}
