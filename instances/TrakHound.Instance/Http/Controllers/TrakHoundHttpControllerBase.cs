// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TrakHound.Http
{
    public abstract class TrakHoundHttpControllerBase : ControllerBase
    {
        public IActionResult ProcessJsonContentResponse(string json)
        {
            var result = new ContentResult();
            result.Content = json;
            result.ContentType = "application/json";
            return result;
        }


        public static async Task<TOutput> GetRequestBodyJson<TOutput>(Stream requestBody)
        {
            // Deserialized from the Stream (instead of reading it into bytes, then a string, then JSON)
            if (requestBody == null) return default;

            try
            {
                return await JsonSerializer.DeserializeAsync<TOutput>(requestBody, Json.SharedOptions);
            }
            catch
            {
                return default;
            }
        }

        public static async Task<string> GetRequestBodyString(Stream requestBody)
        {
            var requestBodyBytes = await GetRequestBodyBytes(requestBody);
            if (requestBodyBytes != null)
            {
                try
                {
                    return Encoding.UTF8.GetString(requestBodyBytes);
                }
                catch { }
            }

            return null;
        }

        public static async Task<byte[]> GetRequestBodyBytes(Stream requestBody)
        {
            if (requestBody != null)
            {
                try
                {
                    using (var readStream = new MemoryStream())
                    {
                        await requestBody.CopyToAsync(readStream);
                        readStream.Seek(0, SeekOrigin.Begin);
                        return readStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
