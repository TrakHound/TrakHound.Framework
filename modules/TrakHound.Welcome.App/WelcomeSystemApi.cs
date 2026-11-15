// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TrakHound.Welcome.App
{
    /// <summary>
    /// Reads the Instance's System API from the browser (wwwroot/welcome.js) so each Request is authorized as the visitor
    /// (their Session Cookie), not as the App. Each System API area is authorized separately, so each is read on its own
    /// </summary>
    public class WelcomeSystemApi
    {
        private const string _prefix = "trakhoundWelcome.";

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly IJSRuntime _jsRuntime;
        private readonly string _baseUrl;


        /// <param name="baseUrl">The Instance's Base URL (including its Base Path)</param>
        public WelcomeSystemApi(IJSRuntime jsRuntime, string baseUrl)
        {
            _jsRuntime = jsRuntime;
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
        }


        public string GetUrl(string path) => _baseUrl + "/" + (path ?? "").TrimStart('/');

        public async Task<WelcomeSection<T>> Get<T>(string path)
        {
            WelcomeResponse response;
            try
            {
                response = await _jsRuntime.InvokeAsync<WelcomeResponse>(_prefix + "get", GetUrl(path));
            }
            catch (Exception ex)
            {
                return WelcomeSection<T>.Failed(ex.Message);
            }

            if (response == null || !response.Success) return WelcomeSection<T>.Failed(response?.Error ?? "The Instance could not be reached");

            switch (response.Status)
            {
                case 401: return new WelcomeSection<T> { Status = WelcomeSectionStatus.SignInRequired };
                case 403: return new WelcomeSection<T> { Status = WelcomeSectionStatus.Denied };
                case 404: return new WelcomeSection<T> { Status = WelcomeSectionStatus.Unavailable };
            }

            if (response.Status < 200 || response.Status >= 300) return WelcomeSection<T>.Failed($"{response.Status} {response.StatusText}");

            try
            {
                var data = JsonSerializer.Deserialize<T>(response.Body ?? "null", _jsonOptions);
                return new WelcomeSection<T> { Status = WelcomeSectionStatus.Loaded, Data = data };
            }
            catch (JsonException)
            {
                return WelcomeSection<T>.Failed("The response could not be read");
            }
        }

        public async Task<bool> Copy(string text)
        {
            try { return await _jsRuntime.InvokeAsync<bool>(_prefix + "copy", text); }
            catch { return false; }
        }


        private class WelcomeResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("error")]
            public string Error { get; set; }

            [JsonPropertyName("status")]
            public int Status { get; set; }

            [JsonPropertyName("statusText")]
            public string StatusText { get; set; }

            [JsonPropertyName("body")]
            public string Body { get; set; }
        }
    }

    public enum WelcomeSectionStatus
    {
        Loading,
        Loaded,

        /// <summary>
        /// The visitor isn't signed in, and the area requires it (401)
        /// </summary>
        SignInRequired,

        /// <summary>
        /// The visitor doesn't have the "read" Permission for the area, or the area is only available on the Instance's machine (403)
        /// </summary>
        Denied,

        /// <summary>
        /// The System API is disabled (or the Instance doesn't have the area) (404)
        /// </summary>
        Unavailable,

        Error
    }

    /// <summary>
    /// One System API area read for the page
    /// </summary>
    public class WelcomeSection<T>
    {
        public WelcomeSectionStatus Status { get; set; }

        public T Data { get; set; }

        public string Error { get; set; }

        public bool IsLoaded => Status == WelcomeSectionStatus.Loaded && Data != null;


        public static WelcomeSection<T> Failed(string error) => new WelcomeSection<T> { Status = WelcomeSectionStatus.Error, Error = error };
    }

    /// <summary>
    /// The parts of the Api Catalog (/_developer/catalog) the page uses : who the visitor is, and where they can sign in
    /// </summary>
    public class WelcomeCatalog
    {
        [JsonPropertyName("identity")]
        public WelcomeIdentity Identity { get; set; }

        [JsonPropertyName("signInProviders")]
        public List<WelcomeSignInProvider> SignInProviders { get; set; }
    }

    public class WelcomeIdentity
    {
        [JsonPropertyName("providerId")]
        public string ProviderId { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class WelcomeSignInProvider
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Relative to the Instance's Base Path
        /// </summary>
        [JsonPropertyName("signInPath")]
        public string SignInPath { get; set; }
    }

    /// <summary>
    /// The parts of an Api's information the page shows (read as JSON, so it doesn't depend on the TrakHound version the package is built with)
    /// </summary>
    public class WelcomeApiItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("route")]
        public string Route { get; set; }

        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        [JsonPropertyName("httpEnabled")]
        public bool HttpEnabled { get; set; }

        /// <summary>
        /// Off, Marked or All
        /// </summary>
        [JsonPropertyName("mcpTools")]
        public string McpTools { get; set; }

        [JsonPropertyName("mcpReadOnly")]
        public bool McpReadOnly { get; set; }
    }

    /// <summary>
    /// The parts of a Service Configuration the page shows. Its Parameters (which can hold connection strings) are never read
    /// </summary>
    public class WelcomeServiceItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }
    }
}
