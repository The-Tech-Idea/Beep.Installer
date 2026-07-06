using Beep.Installer.Models;

namespace Beep.Installer.Tests;

/// <summary>
/// Shared test utilities for tests that exercise the installer builder
/// without the .NET SDK on disk (i.e. without dotnet publish).
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Applies the test-default build flags so the runner does NOT try to
    /// publish a self-contained / embedded-payload installer (which would
    /// require dotnet SDK on the test machine).
    /// </summary>
    public static InstallProject UseTestDefaults(this InstallProject project)
    {
        project.SelfContained = false;
        project.SingleFile = false;
        return project;
    }
}