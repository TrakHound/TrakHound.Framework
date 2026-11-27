// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.CLI.Tests
{
    /// <summary>
    /// Finding a Package directory's Project file : [id].csproj, or the directory's only Project file (ex. a template project
    /// named "example-api-01", whose Package ID is example_api_01)
    /// </summary>
    public class ProjectFileTests : IDisposable
    {
        private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "trakhound-project-file-tests", Guid.NewGuid().ToString("N"));

        public ProjectFileTests() => Directory.CreateDirectory(_directory);

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private string Write(string filename, string contents = Project)
        {
            var path = Path.Combine(_directory, filename);
            File.WriteAllText(path, contents);
            return path;
        }

        [Fact]
        public void TheProjectNamedAfterThePackage_IsUsed()
        {
            var named = Write("Contoso.Hello.csproj");
            Write("Other.csproj");

            Assert.Equal(named, TrakHoundDotNetPackage.FindProjectFile(_directory, "Contoso.Hello", out _));
        }

        [Fact]
        public void TheOnlyProject_IsUsed_WhenItIsNamedDifferently()
        {
            var project = Write("example-api-01.csproj");

            Assert.Equal(project, TrakHoundDotNetPackage.FindProjectFile(_directory, "example_api_01", out var error));
            Assert.Null(error);
        }

        [Fact]
        public void ATemporaryCopy_IsNotAProject()
        {
            var project = Write("example-api-01.csproj");
            Write("example_api_01-0.1.0-dev.20261127054037.csproj");

            Assert.Equal(project, TrakHoundDotNetPackage.FindProjectFile(_directory, "example_api_01", out _));
        }

        [Fact]
        public void AnInterruptedRun_FindsTheProjectByItsOriginalName()
        {
            Write("example-api-01.tmp");
            Write("example_api_01-0.1.0.csproj");
            Write("notes.tmp", "not a project");

            Assert.Equal(Path.Combine(_directory, "example-api-01.csproj"), TrakHoundDotNetPackage.FindProjectFile(_directory, "example_api_01", out _));
        }

        [Fact]
        public void MoreThanOneProject_IsAnError()
        {
            Write("first.csproj");
            Write("second.csproj");

            Assert.Null(TrakHoundDotNetPackage.FindProjectFile(_directory, "example_api_01", out var error));
            Assert.Contains("first.csproj", error);
            Assert.Contains("second.csproj", error);
        }

        [Fact]
        public void NoProject_IsAnError()
        {
            Assert.Null(TrakHoundDotNetPackage.FindProjectFile(_directory, "example_api_01", out var error));
            Assert.Contains("no .csproj", error);
        }

        [Theory]
        [InlineData("Contoso.Hello-0.0.1.3.csproj", true)]
        [InlineData("example_api_01-0.1.0-dev.20261127054037.csproj", true)]
        [InlineData("example-api-01.csproj", false)]
        [InlineData("Contoso.Hello.csproj", false)]
        [InlineData("my-app-2.csproj", false)]
        public void TemporaryCopies_AreRecognizedByTheirVersion(string filename, bool expected)
        {
            Assert.Equal(expected, TrakHoundDotNetPackage.IsTemporaryProjectCopy(Path.Combine(_directory, filename)));
        }
    }
}
