// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    public class ConfigurationIdTests
    {
        [Theory]
        [InlineData("daily-report")]
        [InlineData("Plant1.Daily_Report")]
        [InlineData("3f2b1c4e-5d6a-4b7c-8d9e-0f1a2b3c4d5e")]
        public void Validate_AcceptsPortableIds(string id)
        {
            Assert.Null(ConfigurationId.Validate(id));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("../daily")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("daily report")]
        [InlineData("-daily")]
        [InlineData(".daily")]
        public void Validate_RejectsUnsafeIds(string id)
        {
            Assert.NotNull(ConfigurationId.Validate(id));
        }

        [Fact]
        public void Validate_RejectsLongIds()
        {
            Assert.NotNull(ConfigurationId.Validate(new string('a', ConfigurationId.MaxLength + 1)));
        }

        [Theory]
        [InlineData("Plant 1 Daily", "plant-1-daily")]
        [InlineData("  Nightly -- Cleanup!  ", "nightly-cleanup")]
        [InlineData("Réport", "r-port")]
        [InlineData("***", "job")]
        public void FromName_MakesSlug(string name, string expected)
        {
            Assert.Equal(expected, ConfigurationId.FromName(name, "job"));
        }

        [Fact]
        public void FromName_IsValidId()
        {
            Assert.True(ConfigurationId.IsValid(ConfigurationId.FromName(new string('x', 300))));
        }

        [Fact]
        public void MakeUnique_AddsFirstFreeSuffix()
        {
            Assert.Equal("daily", ConfigurationId.MakeUnique("daily", new[] { "nightly" }));
            Assert.Equal("daily-3", ConfigurationId.MakeUnique("daily", new[] { "Daily", "daily-2" }));
        }

        [Theory]
        [InlineData("daily report", true)] // Saved before the ID rules
        [InlineData("3f2b1c4e-5d6a-4b7c-8d9e-0f1a2b3c4d5e", true)]
        [InlineData("../daily", false)]
        [InlineData("a/b", false)]
        [InlineData("a\\b", false)]
        [InlineData("c:daily", false)]
        [InlineData("", false)]
        public void IsSafeFileName(string id, bool expected)
        {
            Assert.Equal(expected, ConfigurationId.IsSafeFileName(id));
        }
    }
}
