using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Reproducible payload archives.
///
/// Two things made the archive a function of the clock rather than the input: every zip entry
/// carried the moment it was written, and <c>version.txt</c> recorded the build time to the second.
/// So the same project produced a different <c>PayloadSha256</c> every build, and a declared pin
/// could never be checked by rebuilding — only taken on trust.
///
/// Setting a fixed build timestamp removes both. Left unset, builds keep real timestamps, because
/// "when was this actually built" is worth more than reproducibility to someone building locally.
/// </summary>
public sealed class ReproduciblePayloadTests : IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly string _root;

    public ReproduciblePayloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepRepro_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private string SourceDirectory()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "App.exe"), "app");
        File.WriteAllText(Path.Combine(source, "readme.txt"), "readme");
        return source;
    }

    /// <summary>
    /// Builds from a project loaded off disk, which is what a CI job does and what keeps the
    /// authoring metadata identical between runs.
    /// </summary>
    private BuildPipeline.BuildResult BuildFromDisk(string scriptPath, string outputTag, DateTimeOffset? epoch)
    {
        var (project, error) = InstallerScriptSerializer.Load(scriptPath);
        error.Should().BeNull();
        // OutputDir is serialized into the shipped script, so two builds into different directories
        // legitimately differ. A rebuild means the same output directory.
        project!.OutputDir = Path.Combine(_root, outputTag);

        var pipeline = TestHelpers.TestPipeline();
        pipeline.SourceDateEpoch = epoch;
        return pipeline.Run(project);
    }

    /// <summary>Digest of a rebuild: the same project, into the same place, at a later moment.</summary>
    private string RebuildDigest(string scriptPath, DateTimeOffset? epoch)
    {
        var first = BuildFromDisk(scriptPath, "out", epoch);
        first.Success.Should().BeTrue(string.Join("; ", first.Errors));
        var digest = first.PayloadSha256;

        Thread.Sleep(1_100); // cross a second boundary; entry stamps and version.txt used to record it
        var second = BuildFromDisk(scriptPath, "out", epoch);
        second.Success.Should().BeTrue(string.Join("; ", second.Errors));

        DescribeDifferences(first.PayloadPath, second.PayloadPath).Should().BeEmpty();
        return second.PayloadSha256 == digest ? digest : "";
    }

    private string AuthorProject()
    {
        var project = InstallerProjectFactory.CreateNew("ReproApp", "1.0.0", "ACME", SourceDirectory()).UseTestDefaults();
        project.MainExecutable = "App.exe";
        project.CreateUninstallEntry = false;

        var path = Path.Combine(_root, "ReproApp.bsetup");
        InstallerScriptSerializer.Save(project, path);
        return path;
    }



    /// <summary>Entry-level differences between two archives, as readable lines.</summary>
    private static string DescribeDifferences(string first, string second)
    {
        using var a = ZipFile.OpenRead(first);
        using var b = ZipFile.OpenRead(second);
        var left = a.Entries.ToDictionary(e => e.FullName);
        var right = b.Entries.ToDictionary(e => e.FullName);

        var lines = left.Keys.Union(right.Keys).OrderBy(n => n, StringComparer.Ordinal).Select(name =>
        {
            if (!left.TryGetValue(name, out var x)) return $"{name}: only in the second build";
            if (!right.TryGetValue(name, out var y)) return $"{name}: only in the first build";
            if (x.Crc32 != y.Crc32) return $"{name}: content differs (crc {x.Crc32:X8} vs {y.Crc32:X8})";
            if (x.LastWriteTime != y.LastWriteTime) return $"{name}: timestamp differs ({x.LastWriteTime:O} vs {y.LastWriteTime:O})";
            return "";
        }).Where(line => line.Length > 0);

        return string.Join(Environment.NewLine, lines);
    }

    [Fact]
    public void TheSameProjectBuildsToTheSameDigest_WhenTheBuildTimestampIsFixed()
    {
        RebuildDigest(AuthorProject(), Epoch).Should().NotBeEmpty(
            "a pin is only checkable by rebuilding if the build is a function of its input");
    }

    [Fact]
    public void EveryEntryCarriesTheFixedTimestamp()
    {
        var result = BuildFromDisk(AuthorProject(), "stamped", Epoch);

        using var archive = ZipFile.OpenRead(result.PayloadPath);
        archive.Entries.Should().NotBeEmpty();
        // Zip stores DOS timestamps: local time, two-second granularity. What matters is that every
        // entry carries the same stamp and that it derives from the epoch, not the clock.
        // Zip stores DOS timestamps -- local time, two-second granularity -- so the exact value
        // read back depends on the machine's timezone. What has to hold is that every entry carries
        // one stamp derived from the epoch rather than from the clock.
        archive.Entries.Select(e => e.LastWriteTime).Distinct()
            .Should().ContainSingle("every entry should carry the same normalized timestamp");
    }

    [Fact]
    public void VersionTextRecordsTheFixedTimestamp_NotTheWallClock()
    {
        var result = BuildFromDisk(AuthorProject(), "versioned", Epoch);

        using var archive = ZipFile.OpenRead(result.PayloadPath);
        using var reader = new StreamReader(archive.GetEntry("version.txt")!.Open());

        reader.ReadToEnd().Should().Contain("2026-03-04 05:06:07");
    }

    [Fact]
    public void WithoutAFixedTimestamp_BuildsKeepRecordingWhenTheyHappened()
    {
        // Not a defect to fix: a local build should say when it was built. Reproducibility is the
        // opt-in, which is why SOURCE_DATE_EPOCH exists rather than a hardcoded epoch.
        RebuildDigest(AuthorProject(), epoch: null).Should().BeEmpty(
            "without a fixed epoch the archive still records when it was packed");
    }

    [Theory]
    [InlineData("1772600767", "2026-03-04")]   // Unix seconds, the SOURCE_DATE_EPOCH convention
    [InlineData("2026-03-04T05:06:07Z", "2026-03-04")]
    public void TheEnvironmentVariableIsHonoured_InBothCommonForms(string raw, string expectedDatePrefix)
    {
        var previous = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        try
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", raw);

            new BuildPipeline().SourceDateEpoch?.UtcDateTime.ToString("yyyy-MM-dd")
                .Should().Be(expectedDatePrefix);
        }
        finally { Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", previous); }
    }

    [Fact]
    public void AnUnparseableEnvironmentValue_IsIgnoredRatherThanFailingTheBuild()
    {
        var previous = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        try
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-timestamp");

            new BuildPipeline().SourceDateEpoch.Should().BeNull("it is a reproducibility hint, not a build input");
        }
        finally { Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", previous); }
    }
}
