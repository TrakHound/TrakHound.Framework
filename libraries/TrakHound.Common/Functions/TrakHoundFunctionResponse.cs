// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using TrakHound.Logging;

namespace TrakHound.Functions
{
    /// <summary>
    /// The Response returned from a Function Run. Content is returned as string Parameters
    /// </summary>
    public struct TrakHoundFunctionResponse
    {
        private readonly Dictionary<string, string> _parameters = new Dictionary<string, string>();


        /// <summary>
        /// The Id of the Function Run
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// True if the StatusCode is 2xx
        /// </summary>
        public bool Success => StatusCode >= 200 && StatusCode < 300;

        /// <summary>
        /// The Id of the Package that contains the Function
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package that contains the Function
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Id of the Engine that Ran the Function
        /// </summary>
        public string EngineId { get; set; }

        /// <summary>
        /// The Status Code of the Response (HTTP style, 2xx = Success)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The time the Run started (Unix Nanoseconds)
        /// </summary>
        public long Started { get; set; }

        /// <summary>
        /// The time the Run completed (Unix Nanoseconds)
        /// </summary>
        public long Completed { get; set; }

        /// <summary>
        /// CPU time consumed by the Run in milliseconds (0 when CPU Monitoring is disabled)
        /// </summary>
        public double CpuTime { get; set; }

        /// <summary>
        /// The Parameters returned by the Function (Name => Value)
        /// </summary>
        public IReadOnlyDictionary<string, string> Parameters => _parameters;

        /// <summary>
        /// The Log Items written during the Run
        /// </summary>
        public IEnumerable<TrakHoundLogItem> Logs { get; set; }


        /// <summary>
        /// Create a new Response with a StatusCode of 0
        /// </summary>
        public TrakHoundFunctionResponse()
        {
            StatusCode = 0;
        }

        /// <summary>
        /// Create a new Response with the specified Status Code
        /// </summary>
        public TrakHoundFunctionResponse(int statusCode)
        {
            StatusCode = statusCode;
        }

        /// <summary>
        /// Create a new Response with the specified Status Code and a "message" Parameter
        /// </summary>
        public TrakHoundFunctionResponse(int statusCode, string message)
        {
            StatusCode = statusCode;
            AddParameter("message", message);
        }

        /// <summary>
        /// Create a new Response with the specified Status Code and the properties of the Object added as "object.*" Parameters
        /// </summary>
        public TrakHoundFunctionResponse(int statusCode, object obj)
        {
            StatusCode = statusCode;
            AddObjectParameter("object", obj);
        }

        /// <summary>
        /// Create a new Response with the specified Status Code, a "contentType" Parameter, and the content as a Base64 encoded "content" Parameter
        /// </summary>
        public TrakHoundFunctionResponse(int statusCode, byte[] content, string contentType)
        {
            StatusCode = statusCode;
            AddParameter("contentType", contentType);
            if (content != null) AddParameter("content", Convert.ToBase64String(content));
        }


        /// <summary>
        /// Returns true if a Parameter with the specified name exists
        /// </summary>
        public bool ParameterExists(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                return _parameters.ContainsKey(name);
            }

            return false;
        }

        /// <summary>
        /// Gets the value of the Parameter (null if not found)
        /// </summary>
        public string GetParameter(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                return _parameters.GetValueOrDefault(name);
            }

            return null;
        }

        /// <summary>
        /// Gets the value of the Parameter converted to T (default if not found or not convertible)
        /// </summary>
        public T GetParameter<T>(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var value = _parameters.GetValueOrDefault(name);
                if (value != null)
                {
                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch { }
                }
            }

            return default;
        }

        /// <summary>
        /// Adds a Parameter, replacing any existing value (ignored if the name is empty or the value is null)
        /// </summary>
        public void AddParameter(string name, string value)
        {
            if (!string.IsNullOrEmpty(name) && value != null)
            {
                _parameters.Remove(name);
                _parameters.Add(name, value);
            }
        }

        /// <summary>
        /// Adds the public properties of the Object as Parameters named "{name}.{property}" (nested Objects and arrays are flattened)
        /// </summary>
        public void AddObjectParameter(string name, object obj)
        {
            if (!string.IsNullOrEmpty(name) && obj != null)
            {
                var objType = obj.GetType();

                var objProperties = objType.GetProperties();
                if (!objProperties.IsNullOrEmpty())
                {
                    foreach (var property in objProperties)
                    {
                        var propertyValue = property.GetValue(obj);
                        if (propertyValue != null)
                        {
                            var propertyName = $"{name}.{property.Name.ToCamelCase()}";
                            var propertyType = propertyValue.GetType();

                            if (propertyType.IsClass && propertyType != typeof(string))
                            {
                                if (typeof(IEnumerable).IsAssignableFrom(propertyType))
                                {
                                    var arrayIndex = 0;

                                    IEnumerable enumerablePropertyValues = (IEnumerable)propertyValue;
                                    foreach (var enumerablePropertyValue in enumerablePropertyValues)
                                    {
                                        var enumerablePropertyName = $"{propertyName}[{arrayIndex}]";
                                        var enumerablePropertyType = enumerablePropertyValue.GetType();

                                        if (enumerablePropertyType.IsClass && enumerablePropertyType != typeof(string))
                                        {
                                            AddObjectParameter(enumerablePropertyName, enumerablePropertyValue);
                                        }
                                        else
                                        {
                                            AddParameter(enumerablePropertyName, enumerablePropertyValue.ToString());
                                        }

                                        arrayIndex++;
                                    }
                                }
                                else
                                {
                                    AddObjectParameter(propertyName, propertyValue);
                                }  
                            }
                            else
                            {
                                AddParameter(propertyName, propertyValue.ToString());
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds the properties of the JSON object as Parameters (nested objects are flattened using "." and arrays using "[index]")
        /// </summary>
        public void AddJsonParameter(string name, string json)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(json))
            {
                var objectResults = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (objectResults != null)
                {
                    foreach (var key in objectResults.Keys)
                    {
                        var element = (JsonElement)objectResults[key];

                        ProcessJson(key, element);
                    }
                }
            }
        }

        private void ProcessJson(string key, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String: AddParameter(key, element.ToString()); break;

                case JsonValueKind.Number: AddParameter(key, element.ToString()); break;

                case JsonValueKind.Array:

                    var arrayIndex = 0;
                    foreach (var entry in element.EnumerateArray())
                    {
                        switch (entry.ValueKind)
                        {
                            case JsonValueKind.String:
                                AddParameter($"{key}[{arrayIndex}]", entry.ToString());
                                break;

                            case JsonValueKind.Number:
                                AddParameter($"{key}[{arrayIndex}]", entry.ToString());
                                break;

                            case JsonValueKind.Object:

                                //var keyProperty = entry.EnumerateObject().FirstOrDefault(o => o.Value.ValueKind == JsonValueKind.String || o.Value.ValueKind == JsonValueKind.Number);
                                //ProcessJson(path, keyProperty.Value.ToString(), entry);
                                break;
                        }

                        arrayIndex++;
                    }
                    break;

                case JsonValueKind.Object:

                    foreach (var property in element.EnumerateObject())
                    {
                        var propertyKey = $"{key}.{property.Name.ToCamelCase()}";
                        ProcessJson(propertyKey, property.Value);
                    }
                    break;
            }
        }


        /// <summary>
        /// Serialize the Parameters to JSON
        /// </summary>
        public string ToJson(bool indent = true)
        {
            //if (!_parameters.IsNullOrEmpty())
            //{
            //    var jsonOptions = new JsonWriterOptions();
            //    jsonOptions.Indented = indent;

            //    using var stream = new MemoryStream();
            //    using var jsonWriter = new Utf8JsonWriter(stream, jsonOptions);

            //    jsonWriter.WriteStartObject();

            //    foreach (var parameter in _parameters)
            //    {
            //        if (parameter.Key.StartsWith("object"))
            //        {
            //            var propertyName = parameter.Key.Substring("object".Length + 1);
            //            propertyName = GetPropertyName(propertyName);

            //            jsonWriter.WriteString(propertyName, parameter.Value);
            //        }
            //    }

            //    jsonWriter.WriteEndObject();
            //    jsonWriter.Flush();
            //    return Encoding.UTF8.GetString(stream.ToArray());
            //}

            return Json.Convert(_parameters, indented: indent);
        }

        //struct JsonPropertyModel
        //{
        //    public string Name { get; set; }


        
        //}


        private static string GetPropertyName(string key)
        {
            var i = key.IndexOf('.');
            if (i > 0)
            {
                return key.Substring(0, i);
            }

            return key;
        }
    }
}
