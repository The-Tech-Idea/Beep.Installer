using System;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Hosting;
using FluentAssertions;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Reproduces the finding behind P14: <c>/S /DRYRUN</c> on the shipped sample created the install directory and tried to
/// write HKLM. Runs the same wizard graph the silent path builds, with DryRun set, against that sample.
/// </summary>
public sealed class SilentInstallDryRunTests : IDisposable
{
    private readonly string _target = Path.Combine(Path.GetTempPath(), "BeepDryRun_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_target)) Directory.Delete(_target, true); }
        catch (IOException) { /* a leftover temp folder is disposable */ }
    }

    [Fact]
    public void InstallWizard_WithDryRun_CarriesDryRunIntoTheSteps_AndCopiesNoFiles()
    {
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        var (project, error) = InstallerScriptSerializer.Load(script);
        project.Should().NotBeNull(error);
        InstallerScriptSerializer.ResolveRelativePaths(project!, script);

        var context = InstallContextBuilder.ForInstall(project!, _target, perUser: true);
        var options = new SetupOptions { Environment = "Production", DryRun = true };
        var wizard = InstallWizardGraph.BuildInstall("dryrun-repro", options);

        var result = wizard.Run(context);

        context.Options!.DryRun.Should().BeTrue("the wizard must carry DryRun into the steps");
        // Known gap, owned by BeepDM: DirectoryCreateStep and VerifyInstallStep do not check DryRun, so the folder is still
        // created and Verify then reports the files it did not copy. Files and the registry are no longer touched.
        if (Directory.Exists(_target))
            Directory.GetFiles(_target, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".beep-installer", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty("a dry run must not copy files; result: " + result.Message);
        result.Message.Should().NotContain("BI3110", "a dry run must not attempt registry writes");
    }
}
