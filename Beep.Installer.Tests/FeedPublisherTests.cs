using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Updates;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Stage 11.A.2 — the `/PUBLISHFEED` publisher: versioned folder layout, full-installer hash,
/// solid-payload delta store, atomic `feed.json`, and the immutability guard.
/// </summary>
public class FeedPublisherTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;

    public FeedPublisherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "beepfeed_" + Guid.NewGuid().ToString("N"));
        _feed = Path.Combine(_root, "feed");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeExe(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Builds a real solid payload zip (blobs + manifest) from a small source tree.</summary>
    private string MakeSolidZip(string name, params (string rel, string content)[] files)
    {
        var src = Path.Combine(_root, "src_" + Guid.NewGuid().ToString("N")[..6]);
        foreach (var (rel, content) in files)
        {
            var f = Path.Combine(src, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, content);
        }
        var zip = Path.Combine(_root, name);
        PayloadPackager.CreateSolid(src, zip, CompressionLevel.Fastest);
        return zip;
    }

    private static UpdateFeed ReadFeed(string feedFile)
        => JsonSerializer.Deserialize<UpdateFeed>(File.ReadAllText(feedFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private FeedPublisher.Request Req(string version, string exe, string? zip = null, bool republish = false) => new()
    {
        FeedDir = _feed,
        Product = "MyApp",
        Version = version,
        FullExePath = exe,
        PayloadZipPath = zip,
        Republish = republish
    };

    // ── Full installer ──

    [Fact]
    public void Publishes_FullInstaller_WithMatchingHash()
    {
        var exe = MakeExe("Setup-MyApp-1.0.0.exe", "installer-v1-bytes");

        var result = new FeedPublisher().Publish(Req("1.0.0", exe));

        result.Success.Should().BeTrue(result.Summary);
        var published = Path.Combine(_feed, "1.0.0", "Setup-MyApp-1.0.0.exe");
        File.Exists(published).Should().BeTrue();

        var feed = ReadFeed(result.FeedFile);
        feed.Product.Should().Be("MyApp");
        feed.Latest!.Version.Should().Be("1.0.0");
        feed.Latest.Full!.Url.Should().Be("1.0.0/Setup-MyApp-1.0.0.exe");
        feed.Latest.Full.Sha256.Should().Be(Sha(published));
    }

    // ── Two versions ──

    [Fact]
    public void TwoVersions_ProduceBothFolders_FeedPointsAtLatest()
    {
        var pub = new FeedPublisher();
        pub.Publish(Req("1.0.0", MakeExe("Setup-MyApp-1.0.0.exe", "v1"))).Success.Should().BeTrue();
        pub.Publish(Req("1.1.0", MakeExe("Setup-MyApp-1.1.0.exe", "v2"))).Success.Should().BeTrue();

        Directory.Exists(Path.Combine(_feed, "1.0.0")).Should().BeTrue("older versions stay available");
        Directory.Exists(Path.Combine(_feed, "1.1.0")).Should().BeTrue();

        ReadFeed(Path.Combine(_feed, "feed.json")).Latest!.Version.Should().Be("1.1.0");
    }

    // ── Immutability ──

    [Fact]
    public void Republish_IsRefused_WithoutFlag_AndAllowed_WithIt()
    {
        var pub = new FeedPublisher();
        pub.Publish(Req("1.0.0", MakeExe("Setup-MyApp-1.0.0.exe", "v1"))).Success.Should().BeTrue();

        var second = pub.Publish(Req("1.0.0", MakeExe("Setup-MyApp-1.0.0b.exe", "v1-rebuilt")));
        second.Success.Should().BeFalse("a published version is immutable");
        second.Errors.Should().ContainMatch("*already published*");

        pub.Publish(Req("1.0.0", MakeExe("Setup-MyApp-1.0.0c.exe", "v1-forced"), republish: true))
            .Success.Should().BeTrue("/REPUBLISH is the explicit override");
    }

    // ── Delta store ──

    [Fact]
    public void SolidPayload_ExpandsLooseBlobStore_AndSetsDelta()
    {
        var exe = MakeExe("Setup-MyApp-1.0.0.exe", "v1");
        var zip = MakeSolidZip("payload.zip", ("app.dll", "AAAA"), ("docs/readme.txt", "hello"), ("dup.dll", "AAAA"));

        var result = new FeedPublisher().Publish(Req("1.0.0", exe, zip));

        result.Success.Should().BeTrue(result.Summary);
        result.DeltaPublished.Should().BeTrue();

        var versionDir = Path.Combine(_feed, "1.0.0");
        File.Exists(Path.Combine(versionDir, "_payload-manifest.json")).Should().BeTrue();
        var blobs = Directory.GetFiles(Path.Combine(versionDir, "_blobs"));
        blobs.Length.Should().Be(2, "app.dll and dup.dll share content → one blob, plus readme");

        var feed = ReadFeed(result.FeedFile);
        feed.Latest!.Delta!.ManifestUrl.Should().Be("1.0.0/_payload-manifest.json");
        feed.Latest.Delta.BlobBaseUrl.Should().Be("1.0.0/_blobs/");
    }

    [Fact]
    public void NonSolidPayload_PublishesFullOnly_WithWarning()
    {
        var exe = MakeExe("Setup-MyApp-1.0.0.exe", "v1");
        // A plain (non-solid) zip has no _payload-manifest.json.
        var plain = Path.Combine(_root, "plain.zip");
        using (var z = ZipFile.Open(plain, ZipArchiveMode.Create))
            z.CreateEntry("app.dll");

        var result = new FeedPublisher().Publish(Req("1.0.0", exe, plain));

        result.Success.Should().BeTrue();
        result.DeltaPublished.Should().BeFalse();
        result.Warnings.Should().ContainMatch("*not solid*");
        ReadFeed(result.FeedFile).Latest!.Delta.Should().BeNull();
    }

    // ── Base URL ──

    [Fact]
    public void BaseUrl_ProducesAbsoluteArtifactUrls()
    {
        var exe = MakeExe("Setup-MyApp-1.0.0.exe", "v1");
        var req = new FeedPublisher.Request
        {
            FeedDir = _feed, Product = "MyApp", Version = "1.0.0",
            FullExePath = exe, BaseUrl = "https://dl.example.com/myapp/"
        };

        var result = new FeedPublisher().Publish(req);

        ReadFeed(result.FeedFile).Latest!.Full!.Url
            .Should().Be("https://dl.example.com/myapp/1.0.0/Setup-MyApp-1.0.0.exe");
    }

    private static string Sha(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }
}
