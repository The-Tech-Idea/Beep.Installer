using Beep.Installer.Models;
using System;
using System.Collections.Generic;
using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 1 (Track A3.4) — component-condition editor validation.</summary>
public class ConditionListValidatorTests
{
    private static InstallCondition Cond(ConditionType type, string? value = null, string? value2 = null, string? op = null)
        => new() { Type = type, Value = value, Value2 = value2, Operator = op };

    [Fact]
    public void NullList_HasNoIssues()
        => ConditionListValidator.Validate(null).Should().BeEmpty();

    [Fact]
    public void ConditionListValidator_EmptyList_IsEmpty()
        => ConditionListValidator.Validate(new List<InstallCondition>()).Should().BeEmpty();

    [Fact]
    public void Architecture_WithoutValue_IsError()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.ArchitecturesAllowed) })
            .Should().Contain(i => i.Severity == ConditionListValidator.IssueSeverity.Error);

    [Fact]
    public void OsVersion_WithoutValue_IsError()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.OsVersion) })
            .Should().Contain(i => i.Severity == ConditionListValidator.IssueSeverity.Error && i.Message.Contains("OsVersion"));

    [Fact]
    public void FileExists_WithoutValue_IsError()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.FileExists) })
            .Should().Contain(i => i.Severity == ConditionListValidator.IssueSeverity.Error);

    [Fact]
    public void AlwaysTrue_NeverComplains()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.AlwaysTrue) }).Should().BeEmpty();

    [Fact]
    public void IsAdmin_NeverComplains()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.IsAdmin) }).Should().BeEmpty();

    [Fact]
    public void RegistryValue_MissingExpectedValue_IsWarning()
    {
        var issues = ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.RegistryValue, "HKLM\\Software\\Foo", null, "==") });
        issues.Should().Contain(i => i.Severity == ConditionListValidator.IssueSeverity.Warning && i.Message.Contains("Value2"));
    }

    [Fact]
    public void RegistryValue_WithAllFields_IsClean()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.RegistryValue, "HKLM\\Software\\Foo", "1", "==") })
            .Should().BeEmpty();

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData(">=")]
    public void RecognizedOperator_NoWarning(string op)
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.OsVersion, "10.0.0", null, op) })
            .Should().NotContain(i => i.Message.Contains("Operator"));

    [Fact]
    public void UnrecognizedOperator_IsWarning()
        => ConditionListValidator.Validate(new List<InstallCondition> { Cond(ConditionType.OsVersion, "10.0.0", null, "%%") })
            .Should().Contain(i => i.Message.Contains("Operator"));
}
