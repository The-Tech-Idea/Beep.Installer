using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// A build started from the builder can actually be stopped (3.C.2).
///
/// <c>BuildProgressForm</c> has had a Cancel button and a <c>SetCancellationSource</c> method all
/// along, but nothing ever called the latter — so pressing Cancel set the button text to
/// "Cancelling…" and the build ran to completion regardless. The CLI has always passed a token;
/// only the UI path was missing one.
/// </summary>
public class BuildCancellationTests
{
    private static string NewSourceTree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BeepCancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.exe"), "app");
        return root;
    }

    [Fact]
    public void ControllerBuild_AcceptsACancellationToken()
    {
        // A source-level guard: the overload existing is the whole fix. If someone removes the
        // parameter the UI silently goes back to an unstoppable build, which is invisible at runtime
        // until a user tries to cancel one.
        var method = typeof(InstallerController).GetMethod(nameof(InstallerController.Build));

        method.Should().NotBeNull();
        method!.GetParameters().Should().Contain(p => p.ParameterType == typeof(CancellationToken),
            "the UI has no other way to stop a build");
    }

    [Fact]
    public void AnAlreadyCancelledTokenStopsTheBuild_WithoutThrowing()
    {
        var source = NewSourceTree();
        var output = Path.Combine(source, "out");
        try
        {
            var project = InstallerProjectFactory.CreateNew("CancelApp", "1.0.0", "ACME", source);
            project.OutputDir = output;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var pipeline = new BuildPipeline { CancellationToken = cts.Token };
            var result = pipeline.Run(project);

            // The pipeline turns cancellation into a result, not an exception -- the UI shows it as
            // "Build canceled." rather than an error dialog blaming the project.
            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("cancel", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(source, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TheProgressDialogIsGivenACancellationSource()
    {
        // The other half: SetCancellationSource exists, and the builder must call it. Guarded at
        // source level because driving the real dialog needs a message loop.
        var source = ReadRepoFile(Path.Combine("Beep.Installer", "Forms", "PackageBuilderForm.cs"));

        source.Should().Contain("SetCancellationSource(",
            "the Cancel button is inert unless the builder hands the dialog a source");
        source.Should().Contain("_controller.Build(clean, ",
            "the token has to reach the pipeline, not just the dialog");
    }

    [Fact]
    public void ACancelledBuildIsNotReportedAsAFailure()
    {
        // Telling the user "Build failed" after they pressed Cancel reads as a defect in their
        // project rather than as the thing they just asked for.
        var source = ReadRepoFile(Path.Combine("Beep.Installer", "Forms", "PackageBuilderForm.cs"));

        source.Should().Contain("Build canceled.");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        var path = Path.Combine(dir!.FullName, relativePath);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}
