// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text;

namespace TrakHound.ApiExplorer.App
{
    /// <summary>
    /// A Request described by the Explorer (used to create the Code Samples)
    /// </summary>
    public class CodeSampleRequest
    {
        public string Method { get; set; }

        public string Url { get; set; }

        public bool IsStream { get; set; }

        /// <summary>
        /// The Header to send the API Key in (null when no Credentials are sent)
        /// </summary>
        public string ApiKeyHeader { get; set; }

        public string ContentType { get; set; }

        public string Body { get; set; }

        /// <summary>
        /// The Content Type of the Success Response (null if the Endpoint doesn't describe it)
        /// </summary>
        public string ResponseContentType { get; set; }
    }

    /// <summary>
    /// Creates Code Samples for calling an Endpoint. The API Key is always a placeholder (never the developer's real key)
    /// </summary>
    public static class CodeSamples
    {
        public const string ApiKeyPlaceholder = "<your-api-key>";

        public static readonly IReadOnlyList<(string Id, string Title)> Languages = new[]
        {
            ("curl", "cURL"),
            ("javascript", "JavaScript"),
            ("csharp", "C#"),
            ("python", "Python")
        };


        public static string Create(string language, CodeSampleRequest request)
        {
            string sample;
            switch (language)
            {
                case "javascript": sample = request.IsStream ? JavaScriptWebSocket(request) : JavaScript(request); break;
                case "csharp": sample = request.IsStream ? CSharpWebSocket(request) : CSharp(request); break;
                case "python": sample = request.IsStream ? PythonWebSocket(request) : Python(request); break;
                default: sample = request.IsStream ? CurlWebSocket(request) : Curl(request); break;
            }

            return sample.Replace("\r\n", "\n");
        }


        private static bool HasBody(CodeSampleRequest request) => request.Body != null && request.Method != "GET" && request.Method != "HEAD";

        private static string Curl(CodeSampleRequest request)
        {
            var s = new StringBuilder();
            s.Append("curl");
            if (request.Method != "GET") s.Append($" -X {request.Method}");
            s.Append($" \"{request.Url}\"");
            if (request.ApiKeyHeader != null) s.Append($" \\\n  -H \"{request.ApiKeyHeader}: {ApiKeyPlaceholder}\"");
            if (HasBody(request))
            {
                s.Append($" \\\n  -H \"Content-Type: {request.ContentType}\"");
                s.Append($" \\\n  --data-raw '{request.Body.Replace("'", "'\\''")}'");
            }
            return s.ToString();
        }

        private static string CurlWebSocket(CodeSampleRequest request)
        {
            var s = new StringBuilder();
            s.AppendLine("# Stream Endpoints are WebSockets (ex. using websocat : https://github.com/vi/websocat)");
            s.Append($"websocat \"{request.Url}\"");
            if (request.ApiKeyHeader != null) s.Append($" \\\n  -H \"{request.ApiKeyHeader}: {ApiKeyPlaceholder}\"");
            return s.ToString();
        }

        private static string JavaScript(CodeSampleRequest request)
        {
            var headers = new List<string>();
            if (request.ApiKeyHeader != null) headers.Add($"'{request.ApiKeyHeader}': '{ApiKeyPlaceholder}'");
            if (HasBody(request)) headers.Add($"'Content-Type': '{request.ContentType}'");

            var s = new StringBuilder();
            s.AppendLine($"const response = await fetch('{EscapeSingleQuoted(request.Url)}', {{");
            s.Append($"  method: '{request.Method}'");
            if (headers.Count > 0)
            {
                s.AppendLine(",");
                s.AppendLine("  headers: {");
                s.AppendLine("    " + string.Join(",\n    ", headers));
                s.Append("  }");
            }
            if (HasBody(request))
            {
                s.AppendLine(",");
                s.Append($"  body: {ToJavaScriptString(request.Body)}");
            }
            s.AppendLine();
            s.AppendLine("});");
            s.AppendLine();
            s.AppendLine("if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);");
            s.Append(IsJson(request) ? "const data = await response.json();" : "const data = await response.text();");
            return s.ToString();
        }

        private static string JavaScriptWebSocket(CodeSampleRequest request)
        {
            var s = new StringBuilder();
            if (request.ApiKeyHeader != null)
            {
                s.AppendLine("// Browsers can't set Headers on a WebSocket, so it is sent with the Session Cookie of the signed in user.");
                s.AppendLine("// From Node.js (the 'ws' package) the API Key can be sent in a Header :");
                s.AppendLine($"// new WebSocket(url, {{ headers: {{ '{request.ApiKeyHeader}': '{ApiKeyPlaceholder}' }} }})");
            }
            s.AppendLine($"const socket = new WebSocket('{EscapeSingleQuoted(request.Url)}');");
            s.AppendLine();
            s.Append("socket.onmessage = (event) => console.log(event.data);");
            return s.ToString();
        }

        private static string CSharp(CodeSampleRequest request)
        {
            var s = new StringBuilder();
            s.AppendLine("using var client = new HttpClient();");
            if (request.ApiKeyHeader != null) s.AppendLine($"client.DefaultRequestHeaders.Add(\"{request.ApiKeyHeader}\", \"{ApiKeyPlaceholder}\");");
            s.AppendLine();
            s.AppendLine($"var request = new HttpRequestMessage(new HttpMethod(\"{request.Method}\"), \"{EscapeDoubleQuoted(request.Url)}\");");
            if (HasBody(request))
            {
                s.AppendLine($"request.Content = new StringContent({ToCSharpString(request.Body)}, System.Text.Encoding.UTF8, \"{request.ContentType}\");");
            }
            s.AppendLine();
            s.AppendLine("using var response = await client.SendAsync(request);");
            s.AppendLine("response.EnsureSuccessStatusCode();");
            s.Append("var content = await response.Content.ReadAsStringAsync();");
            return s.ToString();
        }

        private static string CSharpWebSocket(CodeSampleRequest request)
        {
            var s = new StringBuilder();
            s.AppendLine("using var socket = new System.Net.WebSockets.ClientWebSocket();");
            if (request.ApiKeyHeader != null) s.AppendLine($"socket.Options.SetRequestHeader(\"{request.ApiKeyHeader}\", \"{ApiKeyPlaceholder}\");");
            s.AppendLine($"await socket.ConnectAsync(new Uri(\"{EscapeDoubleQuoted(request.Url)}\"), CancellationToken.None);");
            s.AppendLine();
            s.AppendLine("var buffer = new byte[64 * 1024];");
            s.AppendLine("while (socket.State == System.Net.WebSockets.WebSocketState.Open)");
            s.AppendLine("{");
            s.AppendLine("    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);");
            s.AppendLine("    Console.WriteLine(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));");
            s.Append("}");
            return s.ToString();
        }

        private static string Python(CodeSampleRequest request)
        {
            var headers = new List<string>();
            if (request.ApiKeyHeader != null) headers.Add($"\"{request.ApiKeyHeader}\": \"{ApiKeyPlaceholder}\"");
            if (HasBody(request)) headers.Add($"\"Content-Type\": \"{request.ContentType}\"");

            var s = new StringBuilder();
            s.AppendLine("import requests");
            s.AppendLine();
            s.AppendLine("response = requests.request(");
            s.AppendLine($"    \"{request.Method}\",");
            s.Append($"    \"{EscapeDoubleQuoted(request.Url)}\"");
            if (headers.Count > 0)
            {
                s.AppendLine(",");
                s.Append($"    headers={{{string.Join(", ", headers)}}}");
            }
            if (HasBody(request))
            {
                s.AppendLine(",");
                s.Append($"    data={ToPythonString(request.Body)}");
            }
            s.AppendLine();
            s.AppendLine(")");
            s.AppendLine("response.raise_for_status()");
            s.Append(IsJson(request) ? "data = response.json()" : "data = response.text");
            return s.ToString();
        }

        private static string PythonWebSocket(CodeSampleRequest request)
        {
            var s = new StringBuilder();
            s.AppendLine("# pip install websockets");
            s.AppendLine("import asyncio, websockets");
            s.AppendLine();
            s.AppendLine("async def main():");
            var headers = request.ApiKeyHeader != null ? $", additional_headers={{\"{request.ApiKeyHeader}\": \"{ApiKeyPlaceholder}\"}}" : "";
            s.AppendLine($"    async with websockets.connect(\"{EscapeDoubleQuoted(request.Url)}\"{headers}) as socket:");
            s.AppendLine("        async for message in socket:");
            s.AppendLine("            print(message)");
            s.AppendLine();
            s.Append("asyncio.run(main())");
            return s.ToString();
        }


        private static bool IsJson(CodeSampleRequest request) => request.ResponseContentType != null && request.ResponseContentType.Contains("json");

        private static string EscapeSingleQuoted(string value) => value?.Replace("\\", "\\\\").Replace("'", "\\'");

        private static string EscapeDoubleQuoted(string value) => value?.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string ToJavaScriptString(string value) => "`" + value.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${") + "`";

        private static string ToCSharpString(string value) => "\"\"\"\n" + value + "\n\"\"\"";

        private static string ToPythonString(string value) => "\"\"\"" + value.Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\\\"\\\"") + "\"\"\"";
    }
}
