using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.Updates;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Covers the opt-in side-by-side install layout: <c>&lt;base&gt;\app-&lt;version&gt;</c> for files,
/// a <c>&lt;base&gt;\current</c> junction shortcuts target, and the runtime install-root patch that
/// lets the app self-update via deltas. A flat install must be byte-identical to before.
/// </summary>
public class SideBySideInstallTests : IDisposable
{
    private sealed class FakeLink : IDirectoryLink
    {
        public readonly Dictionary<string, string> Targets = new(StringComparer.OrdinalIgnoreCase);
        public void Point(string linkPath, string targetPath) => Targets[linkPath] = targetPath;
    }

    private readonly string _root;

    public SideBySideInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "beepsxsinst_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static InstallProject Project(bool sxs) => new()
    {
        AppName = "MyApp",
        AppVersion = "1.2.0",
        SideBySide = sxs,
        Components = new ObservableCollection<InstallComponent>
        {
            new() { Id = "core", Name = "Core", Required = true, Selected = true }
        }
    };

    [Fact]
    public void ForInstall_SideBySide_LaysOutVersionedPaths_AndFlagsContext()
    {
        var baseDir = Path.Combine(_root, "MyApp");
        var ctx = InstallContextBuilder.ForInstall(Project(sxs: true), baseDir, perUser: true);

        ctx.TryGetProperty<string>("InstallPath").Should().Be(Path.Combine(baseDir, "app-1.2.0"));
        ctx.TryGetProperty<string>("LaunchPath").Should().Be(Path.Combine(baseDir, "current"));
        ctx.TryGetProperty<string>("InstallBaseDir").Should().Be(baseDir);
        (ctx.Properties.TryGetValue("SideBySide", out var v) && v is true).Should().BeTrue();
    }

    [Fact]
    public void ForInstall_Flat_KeepsEveryPathEqual()
    {
        var baseDir = Path.Combine(_root, "MyApp");
        var ctx = InstallContextBuilder.ForInstall(Project(sxs: false), baseDir, perUser: true);

        ctx.TryGetProperty<string>("InstallPath").Should().Be(baseDir);
        ctx.TryGetProperty<string>("LaunchPath").Should().Be(baseDir);
        ctx.TryGetProperty<string>("InstallBaseDir").Should().Be(baseDir);
        (ctx.Properties.TryGetValue("SideBySide", out var v) && v is false).Should().BeTrue();
    }

    [Fact]
    public void JunctionStep_LinksCurrent_AndPatchesInstallRoot_PreservingFeed()
    {
        var baseDir = Path.Combine(_root, "MyApp");
        var installPath = Path.Combine(baseDir, "app-1.2.0");
        Directory.CreateDirectory(installPath);
        // The payload shipped an update-settings.json (feed configured, no install root yet).
        File.WriteAllText(Path.Combine(installPath, "update-settings.json"),
            "{\"feedUrl\":\"https://host/feed.json\",\"channel\":\"stable\",\"mode\":\"Optional\",\"currentVersion\":\"1.2.0\"}");

        var ctx = InstallContextBuilder.ForInstall(Project(sxs: true), baseDir, perUser: true);
        var link = new FakeLink();

        var result = new JunctionCreateStep(link: link).Execute(ctx);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        link.Targets[Path.Combine(baseDir, "current")].Should().Be(installPath);

        var patched = JsonSerializer.Deserialize<UpdateSettings>(
            File.ReadAllText(Path.Combine(installPath, "update-settings.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } })!;
        patched.InstallRoot.Should().Be(baseDir, "the runtime install root must be stamped so updates land beside this version");
        patched.FeedUrl.Should().Be("https://host/feed.json", "the authored feed must be preserved");
    }

    [Fact]
    public void JunctionStep_IsSkipped_ForFlatInstall()
    {
        var ctx = InstallContextBuilder.ForInstall(Project(sxs: false), Path.Combine(_root, "MyApp"), perUser: true);
        new JunctionCreateStep().CanSkip(ctx).Should().BeTrue("a flat install has no junction to create");
    }

    [Fact]
    public void Serializer_RoundTrips_SideBySide()
    {
        var path = Path.Combine(_root, "app.bsetup");
        InstallerScriptSerializer.Save(Project(sxs: true), path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);
        loaded.Should().NotBeNull(err);
        loaded!.SideBySide.Should().BeTrue();
    }
}
