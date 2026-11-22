using Beep.Installer.Lang;
using Beep.Installer.Mcp;
using Beep.Installer.Ui;

namespace Beep.Installer.Forms;

/// <summary>
/// The in-app home of the MCP server. The server itself is <c>Beep.Installer.exe /MCP</c>; this dialog shows that command and
/// registers it with Claude Desktop, Codex and Claude Code through <see cref="McpClientRegistrar"/>.
/// </summary>
internal sealed class McpServerDialog : Form
{
    private readonly string _exe;
    private readonly TextBox _command = new() { Name = "McpCommand", ReadOnly = true, Dock = DockStyle.Top };
    private readonly TextBox _log = new() { Name = "McpLog", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true };

    internal McpServerDialog(string installerExe)
    {
        _exe = installerExe;
        Text = T("Mcp_Title", "MCP server for AI assistants");
        Font = InstallerTheme.Body;
        Size = new(780, 520); MinimumSize = new(560, 380);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        StartPosition = FormStartPosition.CenterParent;

        var intro = new Label
        {
            AutoSize = true, MaximumSize = new(720, 0), Dock = DockStyle.Top,
            Text = T("Mcp_Intro", "This installer is an MCP server. An AI assistant starts it with the command below and can then validate, plan, build and inspect installer projects. Running the installer normally never starts it."),
        };
        _command.Text = McpClientRegistrar.CommandLine(_exe);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        buttons.Controls.Add(Make("McpRegisterClaudeDesktop", T("Mcp_ClaudeDesktop", "Add to Claude Desktop"), () => McpClientRegistrar.RegisterClaudeDesktop(_exe)));
        buttons.Controls.Add(Make("McpRegisterCodex", T("Mcp_Codex", "Add to Codex"), () => McpClientRegistrar.RegisterCodex(_exe)));
        buttons.Controls.Add(Make("McpRegisterClaudeCode", T("Mcp_ClaudeCode", "Add to Claude Code"), () => McpClientRegistrar.RegisterClaudeCode(_exe)));
        var copy = new Button { Name = "McpCopy", AutoSize = true, Text = T("Mcp_Copy", "Copy command") };
        copy.Click += (_, _) => { Clipboard.SetText(_command.Text); Append(T("Mcp_Copied", "Command copied.")); };
        buttons.Controls.Add(copy);
        var close = new Button { Name = "McpClose", AutoSize = true, Text = T("Common_Close", "Close"), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(close);
        CancelButton = close;

        var layout = new Panel { Dock = DockStyle.Fill, Padding = new(16) };
        layout.Controls.Add(_log);
        layout.Controls.Add(buttons);
        layout.Controls.Add(_command);
        layout.Controls.Add(intro);
        Controls.Add(layout);
        Append(T("Mcp_Ready", "Choose a client to add this installer to. Each registration backs up the client's file first and leaves its other servers alone."));
    }

    private Button Make(string name, string text, Func<McpRegistrationResult> register)
    {
        var button = new Button { Name = name, AutoSize = true, Text = text };
        button.Click += (_, _) =>
        {
            UseWaitCursor = true;
            try { var result = register(); Append((result.Succeeded ? "✓ " : "✗ ") + result.Message); }
            finally { UseWaitCursor = false; }
        };
        return button;
    }

    private void Append(string line) => _log.AppendText(line + Environment.NewLine);

    private static string T(string key, string english) => LanguageManager.GetOrDefault(key, english);
}
