using System.Collections.Generic;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

public class CustomActionValidatorTests
{
    private static CustomAction A(string path = "cmd.exe", bool required = true, int timeoutMs = 0)
        => new() { Path = path, Required = required, TimeoutMs = timeoutMs, Timing = CustomActionTiming.AfterInstall };

    [Fact]
    public void Validate_Null_ReturnsEmpty()
        => InstallProject.ValidateCustomActions(null).Should().BeEmpty();

    [Fact]
    public void Validate_EmptyPath_Required_ReturnsError()
    {
        var issues = InstallProject.ValidateCustomActions(new List<CustomAction> { A("", required: true) });
        issues.Should().Contain(i => i.IsError && i.Message.Contains("Path is required"));
    }

    [Fact]
    public void Validate_EmptyPath_Optional_ReturnsWarning()
    {
        var issues = InstallProject.ValidateCustomActions(new List<CustomAction> { A("", required: false) });
        issues.Should().Contain(i => !i.IsError && i.Message.Contains("empty"));
    }

    [Fact]
    public void Validate_NegativeTimeout_ReturnsError()
    {
        var issues = InstallProject.ValidateCustomActions(new List<CustomAction> { A(timeoutMs: -1) });
        issues.Should().Contain(i => i.IsError && i.Message.Contains("TimeoutMs"));
    }

    [Fact]
    public void Validate_DuplicatePathAtSameTiming_ReturnsWarning()
    {
        var issues = InstallProject.ValidateCustomActions(new List<CustomAction>
        {
            A("c:\\tool.exe"), A("c:\\tool.exe")
        });
        issues.Should().Contain(i => !i.IsError && i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void IsPublishable_True_WhenOnlyWarnings()
    {
        var actions = new List<CustomAction> { A("a.exe"), A("b.exe") };
        InstallProject.ValidateCustomActions(actions).Should().NotContain(i => i.IsError);
    }

    [Fact]
    public void IsPublishable_False_WhenAnyError()
    {
        var actions = new List<CustomAction> { A("a.exe"), new CustomAction { Path = "", Required = true } };
        InstallProject.ValidateCustomActions(actions).Should().Contain(i => i.IsError);
    }
}
