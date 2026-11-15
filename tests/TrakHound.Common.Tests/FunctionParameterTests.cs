// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Functions;
using TrakHound.Volumes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Function Parameters described with FunctionParameter attributes: how they are read, and how Defaults and checks are applied to a Run's Parameters
    /// </summary>
    public class FunctionParameterTests
    {
        [FunctionParameter("days", Description = "Days to keep", Type = TrakHoundFunctionParameterType.Integer, DefaultValue = "30")]
        [FunctionParameter("table", Required = true, Options = new[] { "function_run", "function_log" })]
        [FunctionParameter("apiKey", Secret = true)]
        [FunctionParameter("DAYS", Description = "A duplicate (ignored)")]
        private class DescribedFunction : TrakHoundFunction
        {
            public DescribedFunction(ITrakHoundFunctionConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume) : base(configuration, client, volume) { }

            protected override Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) => Task.FromResult(Ok());
        }

        private class DerivedFunction : DescribedFunction
        {
            public DerivedFunction() : base(null, null, null) { }
        }


        private static List<TrakHoundFunctionParameterInformation> Definitions => TrakHoundFunctionParameterInformation.Create(typeof(DescribedFunction));


        [Fact]
        public void Attributes_AreReadInOrder_WithoutDuplicates()
        {
            var definitions = Definitions;

            Assert.Equal(new[] { "days", "table", "apiKey" }, definitions.Select(o => o.Name));
            Assert.Equal("Days to keep", definitions[0].Description);
            Assert.Equal(TrakHoundFunctionParameterType.Integer, definitions[0].Type);
            Assert.Equal("30", definitions[0].DefaultValue);
            Assert.True(definitions[1].Required);
            Assert.Equal(new[] { "function_run", "function_log" }, definitions[1].Options);
            Assert.True(definitions[2].Secret);
            Assert.Null(definitions[2].Options);
        }

        [Fact]
        public void Attributes_AreInherited()
        {
            Assert.Equal(3, TrakHoundFunctionParameterInformation.Create(typeof(DerivedFunction)).Count);
        }

        [Fact]
        public void UndescribedFunction_HasNoParameters()
        {
            Assert.Empty(TrakHoundFunctionParameterInformation.Create(typeof(string)));
            Assert.Empty(TrakHoundFunctionParameterInformation.Create(null));
        }

        [Fact]
        public void Defaults_AreAddedForMissingOrEmptyParameters()
        {
            var missing = TrakHoundFunctionParameterInformation.ApplyDefaults(Definitions, new Dictionary<string, string> { ["table"] = "function_run" });
            Assert.Equal("30", missing["days"]);

            var empty = TrakHoundFunctionParameterInformation.ApplyDefaults(Definitions, new Dictionary<string, string> { ["days"] = "" });
            Assert.Equal("30", empty["days"]);

            var passed = TrakHoundFunctionParameterInformation.ApplyDefaults(Definitions, new Dictionary<string, string> { ["days"] = "7", ["other"] = "x" });
            Assert.Equal("7", passed["days"]);
            Assert.Equal("x", passed["other"]); // Undescribed Parameters are kept
        }

        [Fact]
        public void MissingRequiredParameter_IsRefused()
        {
            var error = TrakHoundFunctionParameterInformation.Validate(Definitions, new Dictionary<string, string> { ["days"] = "7" });

            Assert.NotNull(error);
            Assert.Contains("Missing Required Parameter : table", error);
        }

        [Theory]
        [InlineData("days", "seven", "whole number")]
        [InlineData("days", "2.5", "whole number")]
        [InlineData("table", "users", "must be one of")]
        public void ValueThatDoesntFit_IsRefused(string name, string value, string expected)
        {
            var parameters = new Dictionary<string, string> { ["table"] = "function_run", [name] = value };

            Assert.Contains(expected, TrakHoundFunctionParameterInformation.Validate(Definitions, parameters));
        }

        [Fact]
        public void ParametersThatFit_AreAccepted()
        {
            var parameters = new Dictionary<string, string> { ["days"] = "7", ["table"] = "function_log", ["other"] = "anything" };

            Assert.Null(TrakHoundFunctionParameterInformation.Validate(Definitions, parameters));
        }

        [Theory]
        [InlineData(TrakHoundFunctionParameterType.Number, "2.5", true)]
        [InlineData(TrakHoundFunctionParameterType.Number, "abc", false)]
        [InlineData(TrakHoundFunctionParameterType.Boolean, "true", true)]
        [InlineData(TrakHoundFunctionParameterType.Boolean, "yes", false)]
        [InlineData(TrakHoundFunctionParameterType.DateTime, "2026-10-28T08:00:00Z", true)]
        [InlineData(TrakHoundFunctionParameterType.DateTime, "tomorrow", false)]
        [InlineData(TrakHoundFunctionParameterType.Json, "{ \"a\": 1 }", true)]
        [InlineData(TrakHoundFunctionParameterType.Json, "{ a: 1", false)]
        public void Types_AreChecked(TrakHoundFunctionParameterType type, string value, bool valid)
        {
            var definition = new TrakHoundFunctionParameterInformation { Name = "value", Type = type };

            Assert.Equal(valid, TrakHoundFunctionParameterInformation.ValidateValue(definition, value) == null);
        }
    }
}
