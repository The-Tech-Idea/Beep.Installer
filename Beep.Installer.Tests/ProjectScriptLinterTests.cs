using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class ProjectScriptLinterTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectScriptLinterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepInstallerLint_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void LintFile_Strict_FailsUnknownSetupKey()
    {
        var path = WriteScript("""
            [Setup]
            SchemaVersion=1.0
            AppName=LintApp
            AppVersion=1.0.0
            SurpriseFeature=yes
            """);

        var result = ProjectScriptLinter.LintFile(path, new ProjectScriptLintOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI0105" && d.Path == "Setup.SurpriseFeature");
    }

    [Fact]
    public void LintFile_Strict_FailsMissingSchemaVersion()
    {
        var path = WriteScript("""
            [Setup]
            AppName=LintApp
            AppVersion=1.0.0
            """);

        var result = ProjectScriptLinter.LintFile(path, new ProjectScriptLintOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI0107");
    }

    [Fact]
    public void LintFile_AllowsKnownSectionsAndSetupKeys()
    {
        var path = WriteScript("""
            [Setup]
            SchemaVersion=1.0
            AppName=LintApp
            AppVersion=1.0.0

            [Components]
            Name: "core"; Required: yes

            [Files]
            Source: "app.exe"; DestDir: "{app}"; Component: core
            """);

        var result = ProjectScriptLinter.LintFile(path, new ProjectScriptLintOptions { Strict = true });

        result.HasErrors.Should().BeFalse();
    }

    private string WriteScript(string content)
    {
        var path = Path.Combine(_tempDir, "script.bsetup");
        File.WriteAllText(path, content);
        return path;
    }
}
