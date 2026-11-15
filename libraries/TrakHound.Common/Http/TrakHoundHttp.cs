// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Http
{
    /// <summary>
    /// Helper methods for working with TrakHound HTTP Requests and Responses
    /// </summary>
    public static partial class TrakHoundHttp
    {
        /// <summary>
        /// Gets the Api Parameters returned in the "trakhound-parameter-{name}" Headers of the Response, keyed by the PascalCase parameter name (null if none were found)
        /// </summary>
        public static Dictionary<string, string> GetApiParameters(TrakHoundHttpResponse httpResponse)
        {
            Dictionary<string, string> parameters = null;

            if (httpResponse.Headers != null)
            {
                foreach (var header in httpResponse.Headers)
                {
                    if (header.Key != null && header.Key.StartsWith(TrakHoundHttpConstants.ApiParameterHeaderPrefix) && header.Key.Length > TrakHoundHttpConstants.ApiParameterHeaderPrefix.Length + 1)
                    {
                        var parameterName = header.Key.Substring(TrakHoundHttpConstants.ApiParameterHeaderPrefix.Length + 1);
                        parameterName = parameterName.ToPascalCase();
                        if (!string.IsNullOrEmpty(parameterName))
                        {
                            if (parameters == null) parameters = new Dictionary<string, string>();
                            parameters.Remove(parameterName);
                            parameters.Add(parameterName, header.Value?.FirstOrDefault());
                        }
                    }
                }
            }

            return parameters;
        }
    }
}
