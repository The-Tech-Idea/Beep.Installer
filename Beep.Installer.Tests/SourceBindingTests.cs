using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// BeepDM binds to source, not to a same-versioned package (0.A.1).
///
/// <c>Beep.Winform.Controls</c> carries a <c>PackageReference</c> to
/// <c>TheTechIdea.Beep.DataManagementModels</c> at the same assembly version as the source build,
/// and NuGet resolves nearest-to-root. Without the direct <c>ProjectReference</c> the stale package
/// wins and the app fails at runtime with <c>TypeLoadException</c> on newer BeepDM types — the
/// failure `CLAUDE.md` warns about and the reason `NoWarn NU1605` is load-bearing rather than
/// incidental.
///
/// The tracker's answer to this was "purge the poisoned global-cache entries". That is the wrong
/// shape of fix: it is a machine-wide action, it has to be repeated on every machine and every CI
/// runner, and it silently stops being true the moment the package is restored again. CI already
/// asserts the binding by hash; this makes a developer build assert it too, so the check travels
/// with the repository instead of with a machine.
/// </summary>
public class SourceBindingTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return dir!.FullName;
    }

    [Fact]
    public void TheLoadedDataManagementModelsCameFromTheSourceBuild()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "DataManagementModels", StringComparison.OrdinalIgnoreCase));

        if (loaded is null || string.IsNullOrEmpty(loaded.Location))
        {
            // Nothing in this test run has touched a BeepDM type yet; force it.
            _ = typeof(TheTechIdea.Beep.Installer.InstallConfig);
            loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "DataManagementModels", StringComparison.OrdinalIgnoreCase));
        }

        loaded.Should().NotBeNull("the installer cannot run without DataManagementModels");

        var siblingBin = Path.Combine(RepoRoot(), "..", "BeepDM", "DataManagementModelsStandard", "bin");
        if (!Directory.Exists(siblingBin))
        {
            // A checkout without the sibling repo cannot answer this question either way, and
            // failing here would report a missing checkout as a binding defect.
            return;
        }

        var sourceOutputs = Directory
            .EnumerateFiles(siblingBin, "DataManagementModels.dll", SearchOption.AllDirectories)
            .ToList();

        if (sourceOutputs.Count == 0) return;   // the sibling has not been built in this workspace

        var loadedHash = Sha256(loaded!.Location);
        var sourceHashes = sourceOutputs.Select(Sha256).ToList();

        sourceHashes.Should().Contain(loadedHash,
            "the DLL under test matches no BeepDM source build output, which means a stale package " +
            "won the reference — see the NU1605 note in CLAUDE.md. Loaded from: " + loaded.Location);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    [Fact]
    public void CoreReferencesDataManagementModelsDirectly()
    {
        // The direct reference is what makes nearest-to-root resolve to source. It reads like a
        // redundant transitive reference and has been "tidied up" before; this states that it is
        // deliberate so the next tidy-up fails a test rather than a user's runtime.
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "Beep.Installer.Core", "Beep.Installer.Core.csproj"));

        csproj.Should().Contain("DataManagementModels.csproj",
            "Core must reference DataManagementModels directly, not only transitively");
    }
}
