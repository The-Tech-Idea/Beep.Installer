using System.Collections.Generic;
using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase cross-cutting (X5) — real glob matching for include/exclude patterns.</summary>
public class GlobMatcherTests
{
    [Theory]
    [InlineData("app.exe", "*.exe", true)]
    [InlineData("app.exe", "*.dll", false)]
    [InlineData("dir/app.exe", "*.exe", false)]       // single * does not cross '/'
    [InlineData("dir/app.exe", "**/*.exe", true)]     // ** crosses directories
    [InlineData("a/b/c/x.dll", "**/x.dll", true)]
    [InlineData("bin/debug/log.txt", "**/bin/**", true)]
    [InlineData("binary/x.txt", "bin", false)]        // substring 'bin' must NOT match 'binary'
    [InlineData("bin/x", "bin", false)]               // 'bin' matches only the exact segment 'bin'
    [InlineData("bin", "bin", true)]
    [InlineData("app.dll", "*.{dll,exe}", true)]
    [InlineData("app.exe", "*.{dll,exe}", true)]
    [InlineData("app.txt", "*.{dll,exe}", false)]
    [InlineData("a.cs", "?.cs", true)]
    [InlineData("ab.cs", "?.cs", false)]
    [InlineData("a/b/file.cs", "**/[ab].cs", false)]  // [ab] is segment-char; "file.cs" ≠ a/b segment name
    [InlineData("a/a.cs", "**/[ab].cs", true)]
    public void Matches_TruthTable(string path, string pattern, bool expected)
    {
        GlobMatcher.Matches(path, pattern).Should().Be(expected);
    }

    [Fact]
    public void IsExcluded_AggregatesPatterns()
    {
        var excludes = new[] { "**/*.pdb", "**/*.log", "secrets/**" };
        GlobMatcher.IsExcluded("a/b/debug.pdb", excludes).Should().BeTrue();
        GlobMatcher.IsExcluded("run.log", excludes).Should().BeTrue();
        GlobMatcher.IsExcluded("secrets/key.txt", excludes).Should().BeTrue();
        GlobMatcher.IsExcluded("app.exe", excludes).Should().BeFalse();
    }

    [Fact]
    public void IsIncluded_EmptyIncludes_MeansEverything()
    {
        GlobMatcher.IsIncluded("anything.dll", new List<string>()).Should().BeTrue();
        GlobMatcher.IsIncluded("anything.dll", null).Should().BeTrue();
    }

    [Fact]
    public void Build_ExcludePattern_PrunesViaGlob()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepglob_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var src = Path.Combine(tmp, "src");
            Directory.CreateDirectory(Path.Combine(src, "bin"));
            Directory.CreateDirectory(Path.Combine(src, "binary")); // must NOT be excluded by "bin"
            File.WriteAllText(Path.Combine(src, "app.exe"), "x");
            File.WriteAllText(Path.Combine(src, "bin", "app.pdb"), "pdb");
            File.WriteAllText(Path.Combine(src, "binary", "keep.txt"), "keep");

            var project = InstallerProjectFactory.CreateNew("Glob", "1.0.0", "P", src);
            project.SourceExcludes.Clear();
            project.SourceExcludes.Add("**/*.pdb");
            project.SourceExcludes.Add("bin/**");
            project.OutputDir = Path.Combine(tmp, "out");
            project.CompressPayload = false;
            project.CreateUninstallEntry = false;
            project.UseTestDefaults();
            var result = TestHelpers.TestPipeline().Run(project);
            result.Success.Should().BeTrue();

            var payload = Path.Combine(project.OutputDir, "payload");
            // Enumerated payload tree (relative files) for assertions:
            var staged = System.IO.Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
            staged.Should().NotBeEmpty();
            File.Exists(Path.Combine(payload, "app.exe")).Should().BeTrue();
            File.Exists(Path.Combine(payload, "binary", "keep.txt")).Should().BeTrue("'binary' must not be pruned by the 'bin' pattern");
            File.Exists(Path.Combine(payload, "bin", "app.pdb")).Should().BeFalse();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }
}


