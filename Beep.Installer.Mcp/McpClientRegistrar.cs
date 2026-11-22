using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Beep.Installer.Mcp;

/// <summary>What registering the installer with an AI client did.</summary>
public sealed record McpRegistrationResult(bool Succeeded, string Message);

/// <summary>
/// The one owner of "tell an AI client how to start this installer as an MCP server". Each client gets the same entry,
/// <c>&lt;installer exe&gt; /MCP</c>, written the way that client reads it. A file is backed up before it is changed, other
/// servers in it are left alone, and a failure is returned to the caller, never swallowed.
/// </summary>
public static partial class McpClientRegistrar
{
    public const string ServerName = "beep-installer";

    public static string DefaultClaudeDesktopConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");

    public static string DefaultCodexConfigPath
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(home, "config.toml");
        }
    }

    /// <summary>The command a person (or a client's config) runs to start the server.</summary>
    public static string CommandLine(string installerExe) => $"\"{installerExe}\" {InstallerMcpHost.CommandLineSwitch}";

    /// <summary>The JSON a client with an <c>mcpServers</c> object (Claude Desktop, Cursor, ...) needs.</summary>
    public static string ClaudeDesktopSnippet(string installerExe)
        => new JsonObject { ["mcpServers"] = new JsonObject { [ServerName] = ServerEntry(installerExe) } }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static McpRegistrationResult RegisterClaudeDesktop(string installerExe, string? configPath = null)
    {
        var path = configPath ?? DefaultClaudeDesktopConfigPath;
        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                       ?? throw new InvalidDataException($"{path} is not a JSON object.");
                var backup = Backup(path);
                var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
                servers[ServerName] = ServerEntry(installerExe);
                root["mcpServers"] = servers;
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                return new(true, $"Registered in {path}. Backup: {backup}. Restart Claude Desktop to load it.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            root = new JsonObject { ["mcpServers"] = new JsonObject { [ServerName] = ServerEntry(installerExe) } };
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return new(true, $"Created {path} with the server. Restart Claude Desktop to load it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new(false, $"Could not update {path}: {ex.Message}");
        }
    }

    public static McpRegistrationResult RegisterCodex(string installerExe, string? configPath = null)
    {
        var path = configPath ?? DefaultCodexConfigPath;
        try
        {
            var section = $"[mcp_servers.{ServerName}]\r\ncommand = {TomlString(installerExe)}\r\nargs = [{TomlString(InstallerMcpHost.CommandLineSwitch)}]\r\n";
            string text;
            string? backup = null;
            if (File.Exists(path))
            {
                backup = Backup(path);
                text = File.ReadAllText(path);
                text = ExistingSection().IsMatch(text)
                    ? ExistingSection().Replace(text, section.Replace("$", "$$") + "\r\n", 1)
                    : text.TrimEnd() + "\r\n\r\n" + section;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                text = section;
            }

            File.WriteAllText(path, text, new UTF8Encoding(false));
            return new(true, $"Registered in {path}.{(backup is null ? "" : " Backup: " + backup + ".")} Restart Codex to load it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Could not update {path}: {ex.Message}");
        }
    }

    /// <summary>Registers with Claude Code at user scope, through its own <c>claude mcp add</c>.</summary>
    public static McpRegistrationResult RegisterClaudeCode(string installerExe)
    {
        try
        {
            var info = new ProcessStartInfo("claude")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "mcp", "add", "--scope", "user", ServerName, "--", installerExe, InstallerMcpHost.CommandLineSwitch })
                info.ArgumentList.Add(a);

            using var process = Process.Start(info) ?? throw new InvalidOperationException("claude did not start.");
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return new(false, "claude mcp add did not finish within 30 seconds.");
            }

            return process.ExitCode == 0
                ? new(true, output.Trim().Length > 0 ? output.Trim() : "Registered with Claude Code (user scope).")
                : new(false, $"claude mcp add failed (exit {process.ExitCode}): {output.Trim()}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new(false, $"Claude Code (the 'claude' command) was not found on PATH: {ex.Message}");
        }
    }

    private static JsonObject ServerEntry(string installerExe)
        => new() { ["command"] = installerExe, ["args"] = new JsonArray(InstallerMcpHost.CommandLineSwitch) };

    private static string Backup(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var backup = $"{path}.bak-{stamp}";
        for (var n = 1; File.Exists(backup); n++)
            backup = $"{path}.bak-{stamp}-{n}";
        File.Copy(path, backup, overwrite: false);
        return backup;
    }

    private static string TomlString(string value)
        => "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"";

    // The server's own table (and its sub-tables), up to the next table that is not ours, or the end of the file.
    [GeneratedRegex(@"(?ms)^\[mcp_servers\.beep-installer\].*?(?=^\[(?!mcp_servers\.beep-installer[\].])|\z)")]
    private static partial Regex ExistingSection();
}
