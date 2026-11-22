using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Beep.Installer.Core.Cli;
using Beep.Installer.Mcp;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The MCP server's runner against stand-in executables (so exit codes, timeouts and JSON handling can
/// be forced), and the whole server over real stdio: <c>Beep.Installer.exe /MCP</c>.
/// </summary>
public sealed class McpServerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BeepMcp_" + Guid.NewGuid().ToString("N"));

    public McpServerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a stand-in still exiting; the temp folder is disposable */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    // ── runner ───────────────────────────────────────────────────────────────

    private string StandIn(string body)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".bat");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return path;
    }

    private static InstallerCommandRunner RunnerFor(string? exe)
        => new(new InstallerExecutable(exe), CliVerbCatalog.Default);

    [Fact]
    public async Task Runner_ReturnsTheExitCodeAsIs_AndTreats3010AsSuccess()
    {
        var reboot = await RunnerFor(StandIn("echo done\r\nexit /b 3010"))
            .RunAsync(new CliInvocationRequest { Command = "version" }, 30, null, CancellationToken.None);

        reboot.Ran.Should().BeTrue();
        reboot.ExitCode.Should().Be(3010);
        reboot.Succeeded.Should().BeTrue();
        reboot.ExitMeaning.Should().Contain("reboot");

        var rejected = await RunnerFor(StandIn("echo bad args 1>&2\r\nexit /b 2"))
            .RunAsync(new CliInvocationRequest { Command = "version" }, 30, null, CancellationToken.None);

        rejected.ExitCode.Should().Be(2);
        rejected.Succeeded.Should().BeFalse();
        rejected.Stderr.Should().Contain("bad args");
    }

    [Fact]
    public async Task Runner_ParsesJsonStdout_AndKeepsTextWhenItIsNotJson()
    {
        var json = await RunnerFor(StandIn("echo {\"ok\":true}"))
            .RunAsync(new CliInvocationRequest { Command = "list-templates" }, 30, null, CancellationToken.None);

        json.Json.Should().NotBeNull();
        json.Json!.Value.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.Stdout.Should().BeNull("the parsed JSON carries it");

        var text = await RunnerFor(StandIn("echo not json at all"))
            .RunAsync(new CliInvocationRequest { Command = "list-templates" }, 30, null, CancellationToken.None);

        text.Json.Should().BeNull();
        text.Stdout.Should().Contain("not json at all");
    }

    [Fact]
    public async Task Runner_RunsInTheServersDirectory_NotTheExecutablesFolder()
    {
        var result = await RunnerFor(StandIn("echo %CD%"))
            .RunAsync(new CliInvocationRequest { Command = "list-templates", PreferJson = false }, 30, null, CancellationToken.None);

        result.Stdout!.Trim().TrimEnd(Path.DirectorySeparatorChar).Should().Be(Environment.CurrentDirectory.TrimEnd(Path.DirectorySeparatorChar));

        var explicitDir = await RunnerFor(StandIn("echo %CD%"))
            .RunAsync(new CliInvocationRequest { Command = "list-templates", PreferJson = false }, 30, _dir, CancellationToken.None);
        explicitDir.Stdout!.Trim().TrimEnd(Path.DirectorySeparatorChar).Should().Be(_dir.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task Runner_KillsACommandThatOutlivesItsTimeout()
    {
        var watch = Stopwatch.StartNew();
        var result = await RunnerFor(StandIn("ping -n 60 127.0.0.1 >nul"))
            .RunAsync(new CliInvocationRequest { Command = "version" }, 2, null, CancellationToken.None);
        watch.Stop();

        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().BeNull();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Runner_NeverWaitsOnStdin()
    {
        // `set /p` reads stdin; the runner closes it, so the stand-in sees EOF and exits instead of hanging.
        var result = await RunnerFor(StandIn("set /p x=\r\nexit /b 0"))
            .RunAsync(new CliInvocationRequest { Command = "version" }, 20, null, CancellationToken.None);

        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Runner_ExplainsAMissingExecutable_NamingTheSetting()
    {
        var unset = await RunnerFor(null).RunAsync(new CliInvocationRequest { Command = "version" }, 5, null, CancellationToken.None);
        unset.Ran.Should().BeFalse();
        unset.Problems.Should().ContainSingle().Which.Should().Contain("--exe").And.Contain("BEEP_INSTALLER_EXE");

        var wrong = await RunnerFor(Path.Combine(_dir, "nope.exe")).RunAsync(new CliInvocationRequest { Command = "version" }, 5, null, CancellationToken.None);
        wrong.Ran.Should().BeFalse();
        wrong.Problems.Should().ContainSingle().Which.Should().Contain("was not found");
    }

    [Fact]
    public async Task Runner_RefusesBeforeSpawning_WhenTheRequestIsInvalid()
    {
        var marker = Path.Combine(_dir, "ran.txt");
        var result = await RunnerFor(StandIn($"echo x> \"{marker}\""))
            .RunAsync(new CliInvocationRequest { Command = "silent-install" }, 5, null, CancellationToken.None);

        result.Ran.Should().BeFalse();
        File.Exists(marker).Should().BeFalse("a refused request must not start a process");
    }

    // ── stdio, against the real executable ───────────────────────────────────

    [Fact]
    public async Task Stdio_InitializeListAndCall_AgainstTheRealInstaller()
    {
        var installer = Path.Combine(AppContext.BaseDirectory, "Beep.Installer.exe");
        File.Exists(installer).Should().BeTrue("the test output carries the shell");

        await using var client = StdioClient.Start(installer);

        var init = await client.CallAsync("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } });
        init.GetProperty("serverInfo").GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        await client.NotifyAsync("notifications/initialized");

        var tools = (await client.CallAsync("tools/list", new { })).GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        tools.Should().Contain(new[]
        {
            "installer_list_commands", "installer_describe_command", "installer_run", "installer_validate", "installer_plan",
            "installer_preview", "installer_format_readiness", "installer_list_templates", "installer_build", "installer_publish_clickonce", "installer_export_winget",
        });

        var version = await client.ToolAsync("installer_run", new { command = "version" });
        version.GetProperty("exitCode").GetInt32().Should().Be(0);
        version.GetProperty("stdout").GetString().Should().Contain("Beep Installer");

        var templates = await client.ToolAsync("installer_list_templates", new { });
        templates.GetProperty("json").GetProperty("Templates").GetArrayLength().Should().BeGreaterThan(0);

        var refusal = await client.ToolAsync("installer_run", new { command = "uninstall", options = new Dictionary<string, string> { ["/SCRIPT="] = "x.bsetup" } });
        refusal.GetProperty("ran").GetBoolean().Should().BeFalse();
        refusal.GetProperty("problems")[0].GetString().Should().Contain("changes this machine");

        // /S /DRYRUN is not a safe preview in the shell (it creates the directory and writes the registry), so MCP must not offer it.
        var noPreview = await client.ToolAsync("installer_run", new { command = "silent-install", options = new Dictionary<string, string> { ["/SCRIPT="] = "x.bsetup" }, dryRun = true });
        noPreview.GetProperty("ran").GetBoolean().Should().BeFalse();
        noPreview.GetProperty("problems")[0].GetString().Should().Contain("does not support /DRYRUN");

        // The read-only authoring tools end to end on the shipped sample: each must run and never be rejected (exit 2).
        var sampleProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        foreach (var tool in new[] { "installer_validate", "installer_plan", "installer_preview", "installer_format_readiness" })
        {
            var run = await client.ToolAsync(tool, new { script = sampleProject });
            run.GetProperty("ran").GetBoolean().Should().BeTrue(tool + ": " + run);
            run.GetProperty("exitCode").GetInt32().Should().BeOneOf(new[] { 0, 1 }, tool + ": " + run);
        }

        // One machine-changing verb for real, through MCP: the shipped /SELFTEST installs, verifies and uninstalls a
        // generated app per-user inside %TEMP%, so it exercises the allowMachineChanges path without touching Program Files or HKLM.
        var selfTest = await client.ToolAsync("installer_run", new { command = "selftest", allowMachineChanges = true, timeoutSeconds = 240 });
        selfTest.GetProperty("ran").GetBoolean().Should().BeTrue(selfTest.ToString());
        selfTest.GetProperty("exitCode").GetInt32().Should().Be(0, selfTest.ToString());

        var prompts = (await client.CallAsync("prompts/list", new { })).GetProperty("prompts").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).ToList();
        prompts.Should().Contain(new[] { "build_installer", "preview_machine_change", "explore_commands" });
        var prompt = await client.CallAsync("prompts/get", new { name = "build_installer", arguments = new { script = "a.bsetup" } });
        prompt.GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString().Should().Contain("installer_validate");

        var describedEffect = await client.ToolAsync("installer_describe_command", new { command = "version" });
        describedEffect.GetProperty("command").GetProperty("effect").GetString().Should().Be("read-only");

        var listed = await client.ToolAsync("installer_list_commands", new { });
        listed.GetProperty("commands").GetArrayLength().Should().Be(CliVerbCatalog.Default.Verbs.Count(v => v.Mcp.Exposed));
    }

    [Fact]
    public async Task Stdio_ScriptQualificationCommands_RunEndToEndOnTheSampleProject()
    {
        var installer = Path.Combine(AppContext.BaseDirectory, "Beep.Installer.exe");
        File.Exists(installer).Should().BeTrue();
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        await using var client = StdioClient.Start(installer);
        await client.CallAsync("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } });
        await client.NotifyAsync("notifications/initialized");

        // Evidence-writing qualification commands: each writes only into a temp folder. They may report findings (exit 1)
        // but must run and must never be rejected (exit 2) or time out.
        foreach (var command in new[] { "qualify-plan", "qualify-config", "qualify-diagnostics", "qualify-security", "qualify-sdk", "qualify-cli" })
        {
            var outDir = Path.Combine(_dir, command);
            var run = await client.ToolAsync("installer_run", new
            {
                command,
                value = sample,
                options = new Dictionary<string, string> { ["/OUT="] = outDir },
                timeoutSeconds = 240,
            });
            run.GetProperty("ran").GetBoolean().Should().BeTrue(command + ": " + run);
            run.GetProperty("timedOut").GetBoolean().Should().BeFalse(command);
            run.GetProperty("exitCode").GetInt32().Should().BeOneOf(new[] { 0, 1 }, command + ": " + run);
        }
    }

    [Fact]
    public async Task Stdio_EvidenceRootQualificationCommands_RunOnAnEmptyRoot_AndReportFindings()
    {
        var installer = Path.Combine(AppContext.BaseDirectory, "Beep.Installer.exe");
        await using var client = StdioClient.Start(installer);
        await client.CallAsync("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } });
        await client.NotifyAsync("notifications/initialized");

        var root = Path.Combine(_dir, "evidence");
        Directory.CreateDirectory(root);
        foreach (var command in new[] { "qualify-vm", "qualify-release" })
        {
            var run = await client.ToolAsync("installer_run", new
            {
                command,
                value = root,
                options = new Dictionary<string, string> { ["/OUT="] = Path.Combine(_dir, command + ".json") },
                timeoutSeconds = 120,
            });
            run.GetProperty("ran").GetBoolean().Should().BeTrue(command + ": " + run);
            run.GetProperty("timedOut").GetBoolean().Should().BeFalse(command);
            run.GetProperty("exitCode").GetInt32().Should().BeOneOf(new[] { 0, 1 }, command + ": " + run);
        }
    }

    private sealed class StdioClient : IAsyncDisposable
    {
        private readonly Process _process;
        private int _id;

        private StdioClient(Process process) => _process = process;

        public static StdioClient Start(string installerExe)
        {
            // The installer is the server: Beep.Installer.exe /MCP.
            var info = new ProcessStartInfo(installerExe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            info.ArgumentList.Add(InstallerMcpHost.CommandLineSwitch);

            var process = Process.Start(info)!;
            _ = process.StandardError.ReadToEndAsync();
            return new StdioClient(process);
        }

        public async Task<JsonElement> CallAsync(string method, object parameters)
        {
            var id = ++_id;
            await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters });
            while (true)
            {
                var line = await ReadLineAsync();
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id) continue;
                if (doc.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
                return doc.RootElement.GetProperty("result").Clone();
            }
        }

        public Task NotifyAsync(string method) => SendAsync(new { jsonrpc = "2.0", method });

        /// <summary>Calls a tool and returns the JSON object it printed as text content.</summary>
        public async Task<JsonElement> ToolAsync(string name, object arguments)
        {
            var result = await CallAsync("tools/call", new { name, arguments });
            using var doc = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
            return doc.RootElement.Clone();
        }

        private async Task SendAsync(object message)
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
            await _process.StandardInput.FlushAsync();
        }

        private async Task<string> ReadLineAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            return await _process.StandardOutput.ReadLineAsync(cts.Token)
                ?? throw new InvalidOperationException("The MCP server closed stdout.");
        }

        public async ValueTask DisposeAsync()
        {
            try { _process.StandardInput.Close(); } catch (IOException) { /* already closed */ }
            if (!_process.WaitForExit(10_000)) _process.Kill(entireProcessTree: true);
            _process.Dispose();
            await Task.CompletedTask;
        }
    }
}
