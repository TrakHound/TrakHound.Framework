// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
	/// <summary>
	/// Identifies a Log written by a Function
	/// </summary>
	public class TrakHoundFunctionLogInformation
	{
		/// <summary>
		/// The unique Identifier of the Log
		/// </summary>
		[JsonPropertyName("id")]
		public string Id { get; set; }

		/// <summary>
		/// The Name of the Log
		/// </summary>
		[JsonPropertyName("name")]
		public string Name { get; set; }
	}
}
