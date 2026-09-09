using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Authoring errors reach the field that caused them (6.C.2), and the builder, `/VALIDATE` and
/// `/BUILD` all answer with the same validator (3.C.1).
///
/// The builder had no inline validation at all: the only way to learn a project was invalid was to
/// build it and read a list of messages naming properties, so the author fixed things somewhere
/// other than where the problem was. Worse, `/BUILD` did not consult the schema validator, so the
/// three could disagree about whether the very same project was valid.
/// </summary>
public class InlineValidationTests
{
    private static InstallProject ValidProject(string sourceDir)
        => InstallerProjectFactory.CreateNew("FieldApp", "1.0.0", "ACME", sourceDir);

    [Fact]
    public void TheReadinessIndicatorUsesTheSameValidatorAsTheBuild()
    {
        // The status bar answers "can I build this yet?" continuously. If it used a different
        // validator from the build it would eventually disagree with it -- which is precisely the
        // split that once let /VALIDATE reject a project /BUILD accepted.
        var source = ReadRepoFile(System.IO.Path.Combine("Beep.Installer", "Forms", "PackageBuilderForm.cs"));
        var readiness = Between(source, "private void RefreshReadiness()", "private Panel BuildIdentitySection");

        readiness.Should().Contain("ProjectSchemaService.Validate(_project)",
            "the indicator must agree with what pressing Build would do");
        readiness.Should().Contain("ProjectSchemaDiagnosticSeverity.Error",
            "only errors block a build; warnings must not be reported as blocking");
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, $"'{start}' should exist");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        return to > from ? source[from..to] : source[from..];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, relativePath));
    }

    [Fact]
    public void SchemaDiagnosticsNameThePropertyTheyAreAbout()
    {
        // This is the seam inline validation relies on: a path of "Setup.<Property>", whose last
        // segment matches the bound control's Name. If diagnostics stopped carrying paths, the
        // error icons would silently stop appearing rather than fail anything.
        var project = ValidProject("");
        project.AppVersion = "";

        var result = ProjectSchemaService.Validate(project);

        var paths = result.Diagnostics.Select(d => d.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        paths.Should().NotBeEmpty("inline validation matches diagnostics to fields by path");
        paths.Should().Contain(p => p.EndsWith("." + nameof(InstallProject.AppVersion)));
    }

    [Fact]
    public void EveryDiagnosticPathSegmentLooksLikeAProperty()
    {
        // Guards the "Setup.AppVersion" -> "AppVersion" split the form does.
        var project = ValidProject("");
        project.AppName = "";
        project.AppVersion = "not-a-version";

        var result = ProjectSchemaService.Validate(project);

        foreach (var diagnostic in result.Diagnostics.Where(d => !string.IsNullOrWhiteSpace(d.Path)))
        {
            var property = diagnostic.Path[(diagnostic.Path.LastIndexOf('.') + 1)..];
            property.Should().NotBeNullOrWhiteSpace($"'{diagnostic.Path}' must end in a name");
            property.Should().NotContain(" ");
        }
    }

    [Fact]
    public void TheBuildRefusesWhatTheSchemaValidatorRejects()
    {
        // The 3.C.1 regression: BuildPipeline had its own five rules and never called the schema
        // validator, so a project /VALIDATE rejected could still be built.
        var root = Path.Combine(Path.GetTempPath(), $"BeepInline_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = ValidProject(root);
            project.SchemaVersion = "99.0";   // a version this build does not support

            var schema = ProjectSchemaService.Validate(project);
            schema.Diagnostics.Should().Contain(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error,
                "the schema validator must object to an unsupported schema version");

            var build = new BuildPipeline().Validate(project);

            build.Errors.Should().NotBeEmpty("the build must not accept what validation rejects");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void AProjectTheSchemaAcceptsStillPassesTheBuildsOwnRules()
    {
        // The other direction: folding schema validation into the build must not start rejecting
        // projects that were always fine.
        var root = Path.Combine(Path.GetTempPath(), $"BeepInline_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.exe"), "app");
        try
        {
            var project = ValidProject(root);

            var build = new BuildPipeline().Validate(project);

            build.Errors.Should().BeEmpty(string.Join("; ", build.Errors));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }
}
