using Beep.Installer.Models;
using FluentAssertions;
using Xunit;
using A11y = Beep.Installer.Engine.Accessibility;

namespace Beep.Installer.Tests;

/// <summary>
/// Phase cross-cutting (X2) — accessibility helpers. The control-tree walks (auto-names, tab
/// order, high-contrast colors) operate on live <see cref="System.Windows.Forms.Control"/> trees
/// and are wired into the main forms via <c>Accessibility.EnsureAccessibility</c>; they cannot be
/// exercised here because instantiating WinForms controls in this test host trips a DPI/OsVersion
/// runtime init. The pure mnemonic-stripping logic is unit-tested below.
/// </summary>
public class AccessibilityTests
{
    [Theory]
    [InlineData("&File", "File")]
    [InlineData("I &agree", "I agree")]
    [InlineData("A && B", "A & B")]      // doubled ampersand is a literal '&'
    [InlineData("None", "None")]
    [InlineData("Save &As…", "Save As…")]
    public void StripMnemonic_HandlesAmpersands(string input, string expected)
        => A11y.StripMnemonic(input).Should().Be(expected);
}
