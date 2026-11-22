using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Beep.Installer.Mcp;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Registration writes the same entry (installer exe + /MCP) into each client's file without disturbing the rest. Temp files only.</summary>
public sealed class McpClientRegistrarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BeepMcpReg_" + Guid.NewGuid().ToString("N"));
    private const string Exe = @"C:\Apps\Beep Installer\Beep.Installer.exe";

    public McpClientRegistrarTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { /* temp */ } }

    [Fact]
    public void ClaudeDesktop_AddsTheServer_KeepsOthers_AndBacksUp()
    {
        var path = Path.Combine(_dir, "claude_desktop_config.json");
        File.WriteAllText(path, "{ \"mcpServers\": { \"other\": { \"command\": \"x\" } }, \"theme\": \"dark\" }");

        var result = McpClientRegistrar.RegisterClaudeDesktop(Exe, path);

        result.Succeeded.Should().BeTrue(result.Message);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["theme"]!.GetValue<string>().Should().Be("dark");
        root["mcpServers"]!["other"]!["command"]!.GetValue<string>().Should().Be("x");
        root["mcpServers"]![McpClientRegistrar.ServerName]!["command"]!.GetValue<string>().Should().Be(Exe);
        root["mcpServers"]![McpClientRegistrar.ServerName]!["args"]![0]!.GetValue<string>().Should().Be("/MCP");
        Directory.GetFiles(_dir, "*.bak-*").Should().HaveCount(1);
    }

    [Fact]
    public void ClaudeDesktop_RefusesAFileThatIsNotJson_AndLeavesItAlone()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "not json");

        var result = McpClientRegistrar.RegisterClaudeDesktop(Exe, path);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(path).Should().Be("not json");
    }

    [Fact]
    public void Codex_AppendsThenReplacesItsOwnSection_WithoutTouchingOtherTables()
    {
        var path = Path.Combine(_dir, "config.toml");
        File.WriteAllText(path, "model = \"x\"\r\n\r\n[mcp_servers.other]\r\ncommand = \"y\"\r\n");

        McpClientRegistrar.RegisterCodex(Exe, path).Succeeded.Should().BeTrue();
        McpClientRegistrar.RegisterCodex(@"D:\New\Beep.Installer.exe", path).Succeeded.Should().BeTrue();

        var text = File.ReadAllText(path);
        text.Should().Contain("model = \"x\"").And.Contain("[mcp_servers.other]").And.Contain("command = \"y\"");
        text.Split("[mcp_servers.beep-installer]").Length.Should().Be(2, "the second registration replaces the first");
        text.Should().Contain("D:/New/Beep.Installer.exe").And.NotContain("C:/Apps/Beep Installer").And.Contain("args = [\"/MCP\"]");
    }

    [Fact]
    public void CommandLine_QuotesThePathAndAddsTheSwitch()
        => McpClientRegistrar.CommandLine(Exe).Should().Be("\"" + Exe + "\" /MCP");
}
