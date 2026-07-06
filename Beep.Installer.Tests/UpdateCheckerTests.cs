using Beep.Installer.Models;
using System;
using System.Xml.Linq;
using Beep.Installer.Engine.ClickOnce;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B3.1) — on-launch ClickOnce update check.</summary>
public class UpdateCheckerTests
{
    private static string MakeDeploymentManifest(string version, string? description = null)
    {
        var asmV1 = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
        var root = new XElement(asmV1 + "assembly", new XAttribute("manifestVersion", "1.0"),
            new XElement(asmV1 + "assemblyIdentity",
                new XAttribute("name", "MyApp"),
                new XAttribute("version", version),
                new XAttribute("type", "application")));
        if (description != null)
            root.Add(new XElement(asmV1 + "description", description));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }

    [Fact]
    public void Remote_Newer_ReportsAvailable()
    {
        var info = UpdateChecker.Check(
            "https://host/MyApp.application",
            currentVersion: "1.0.0.0",
            fetcher: _ => MakeDeploymentManifest("1.2.0.0", "New features"));

        info.Available.Should().BeTrue();
        info.RemoteVersion.Should().Be("1.2.0.0");
        info.Notes.Should().Be("New features");
        info.Error.Should().BeNull();
    }

    [Fact]
    public void Remote_SameVersion_NotAvailable()
    {
        var info = UpdateChecker.Check("https://host/MyApp.application", "1.0.0.0",
            _ => MakeDeploymentManifest("1.0.0.0"));
        info.Available.Should().BeFalse();
        info.RemoteVersion.Should().Be("1.0.0.0");
    }

    [Fact]
    public void Remote_Older_NotAvailable()
    {
        var info = UpdateChecker.Check("https://host/MyApp.application", "2.0.0.0",
            _ => MakeDeploymentManifest("1.5.0.0"));
        info.Available.Should().BeFalse();
    }

    [Fact]
    public void Short_Version_Normalizes_To_Four_Octets()
    {
        var newer = UpdateChecker.Check("u", "1.0", _ => MakeDeploymentManifest("1.0.1"));
        newer.Available.Should().BeTrue("1.0.0.1 vs 1.0.0.0");

        var same = UpdateChecker.Check("u", "1.0", _ => MakeDeploymentManifest("1"));
        same.Available.Should().BeFalse("1.0.0.0 vs 1.0.0.0");
    }

    [Fact]
    public void Malformed_Manifest_Surfaces_Error_Not_Available()
    {
        var info = UpdateChecker.Check("https://host/MyApp.application", "1.0.0.0",
            _ => "{ this is not valid xml }");
        info.Available.Should().BeFalse();
        info.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Fetcher_Exception_Becomes_Error()
    {
        var info = UpdateChecker.Check("https://host/MyApp.application", "1.0.0.0",
            _ => throw new InvalidOperationException("network down"));
        info.Available.Should().BeFalse();
        info.Error.Should().Contain("network down");
    }

    [Theory]
    [InlineData("1.0.0.0", "1.0.0.1", true)]
    [InlineData("1.0.0.1", "1.0.0.0", false)]
    [InlineData("1.2.3.4", "2.0.0.0", true)]
    [InlineData("2.0.0.0", "1.99.99.99", false)]
    [InlineData("1.0.0.0", "1.0.0.0", false)]
    public void IsNewer_Compares_FourPart_Versions(string current, string remote, bool expected)
        => UpdateChecker.IsNewer(current, remote).Should().Be(expected);
}