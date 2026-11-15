// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;

namespace TrakHound.Api
{
    public class TrakHoundApiHttpResult : ActionResult
    {
        private readonly TrakHoundApiResponse _response;


        public TrakHoundApiHttpResult(TrakHoundApiResponse response)
        {
            _response = response;
        }


        public async override void ExecuteResult(ActionContext context)
        {
            if (_response.IsValid())
            {
                try
                {
                    // Set Parameter Headers
                    if (!_response.Parameters.IsNullOrEmpty())
                    {
                        foreach (var parameter in _response.Parameters)
                        {
                            if (!string.IsNullOrEmpty(parameter.Key) && !string.IsNullOrEmpty(parameter.Value))
                            {
                                context.HttpContext.Response.Headers[parameter.Key] = parameter.Value;
                                //var headerName = $"{TrakHoundHttpConstants.ApiParameterHeaderPrefix}-{parameter.Key.ToKebabCase()}";
                                //context.HttpContext.Response.Headers[headerName] = parameter.Value;
                            }
                        }
                    }

                    // Set Status Code
                    context.HttpContext.Response.StatusCode = _response.StatusCode;

                    // Set Content
                    if (_response.Content != null)
                    {
                        context.HttpContext.Response.Headers["Content-Type"] = _response.ContentType;
                        // Streamed Content (ex. a Blob download) may not know its Length
                        if (_response.Content.CanSeek) context.HttpContext.Response.Headers["Content-Length"] = _response.Content.Length.ToString();

                        // Write to Response Body
                        await _response.Content.CopyToAsync(context.HttpContext.Response.Body);
                    }
                }
                catch { }
            }
        }
    }
}
