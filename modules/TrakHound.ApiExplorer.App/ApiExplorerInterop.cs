// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TrakHound.ApiExplorer.App
{
    /// <summary>
    /// Sends Requests from the browser (wwwroot/api-explorer.js) so they use the developer's own Session Cookie or API Key
    /// </summary>
    public class ApiExplorerInterop
    {
        private const string _prefix = "trakhoundApiExplorer.";

        private readonly IJSRuntime _jsRuntime;


        public ApiExplorerInterop(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }


        public async Task<string> GetApiKey()
        {
            try { return await _jsRuntime.InvokeAsync<string>(_prefix + "getApiKey"); }
            catch { return null; }
        }

        public async Task SetApiKey(string apiKey)
        {
            try { await _jsRuntime.InvokeVoidAsync(_prefix + "setApiKey", apiKey); }
            catch { }
        }

        public async Task<ApiExplorerResponse> Send(string url, string method, Dictionary<string, string> headers, string body = null)
        {
            try
            {
                return await _jsRuntime.InvokeAsync<ApiExplorerResponse>(_prefix + "send", url, method, headers ?? new Dictionary<string, string>(), body);
            }
            catch (Exception ex)
            {
                return new ApiExplorerResponse { Success = false, Error = ex.Message };
            }
        }

        public async Task SaveResponse(string filename)
        {
            try { await _jsRuntime.InvokeVoidAsync(_prefix + "saveResponse", filename); }
            catch { }
        }

        public async Task SaveText(string text, string filename, string contentType)
        {
            try { await _jsRuntime.InvokeVoidAsync(_prefix + "saveText", text, filename, contentType); }
            catch { }
        }

        public async Task<bool> Copy(string text)
        {
            try { return await _jsRuntime.InvokeAsync<bool>(_prefix + "copy", text); }
            catch { return false; }
        }
    }

    public class ApiExplorerResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("statusText")]
        public string StatusText { get; set; }

        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        [JsonPropertyName("headers")]
        public List<ApiExplorerHeader> Headers { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonPropertyName("isText")]
        public bool IsText { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("durationMs")]
        public long DurationMs { get; set; }


        public bool IsSuccessStatus => Status >= 200 && Status < 300;
    }

    public class ApiExplorerHeader
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }
    }
}
