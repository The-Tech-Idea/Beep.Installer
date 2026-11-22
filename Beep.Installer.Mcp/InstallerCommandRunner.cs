using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Beep.Installer.Core.Cli;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beep.Installer.Mcp;

/// <summary>
/// The one owner of "run a Beep.Installer command": it validates the request through the shared
/// catalog and argument builder, spawns the executable, and returns the exit code untouched.
///
/// It never re-implements a verb. The executable owns behaviour, so what an AI client gets is exactly
/// what a person at a prompt gets. The child's stdin is closed (it can never wait on a prompt) and its
/// stdout/stderr are captured, so the server's own stdout carries only the MCP protocol.
/// </summary>
public sealed class InstallerCommandRunner
{
    public const int DefaultTimeoutSeconds = 300;
    public const int MaxTimeoutSeconds = 3600;
    private const int MaxCapturedChars = 1_000_000;

    private static readonly JsonDocumentOptions JsonParse = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private readonly InstallerExecutable _executable;
    private readonly CliVerbCatalog _catalog;
    private readonly ILogger<InstallerCommandRunner> _logger;

    public InstallerCommandRunner(InstallerExecutable executable, CliVerbCatalog catalog, ILogger<InstallerCommandRunner>? logger = null)
    {
        _executable = executable;
        _catalog = catalog;
        _logger = logger ?? NullLogger<InstallerCommandRunner>.Instance;
    }

    public CliVerbCatalog Catalog => _catalog;

    public async Task<InstallerRunResult> RunAsync(
        CliInvocationRequest request,
        int? timeoutSeconds,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var invocation = CliInvocationBuilder.Build(_catalog, request);
        if (!invocation.IsValid)
            return InstallerRunResult.Refused(request.Command, invocation.Arguments, invocation.Problems);

        if (!_executable.TryResolve(out var exe, out var problem))
            return InstallerRunResult.Refused(request.Command, invocation.Arguments, new[] { problem });

        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            return InstallerRunResult.Refused(request.Command, invocation.Arguments, new[] { $"workingDirectory '{workingDirectory}' does not exist." });

        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? DefaultTimeoutSeconds, 1, MaxTimeoutSeconds));
        return await SpawnAsync(exe, invocation, workingDirectory, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<InstallerRunResult> SpawnAsync(
        string exe, CliInvocation invocation, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // The server's own directory, not the executable's: relative paths a client passes (a project, an
            // /OUT= folder) mean what they mean to the client's session, not a folder inside bin\.
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
        };
        foreach (var arg in invocation.Arguments) info.ArgumentList.Add(arg);

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = info };
        process.Start();
        process.StandardInput.Close();

        var stdout = new BoundedCapture();
        var stderr = new BoundedCapture();
        var pumps = Task.WhenAll(stdout.PumpAsync(process.StandardOutput), stderr.PumpAsync(process.StandardError));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Both a timeout and a client cancel end here. The whole tree goes: the installer spawns
            // children (msiexec, signtool, dotnet) that must not outlive the call.
            timedOut = !cancellationToken.IsCancellationRequested;
            _logger.LogWarning("Installer command '{Command}' {Reason}; terminating its process tree.",
                invocation.Verb!.Id, timedOut ? $"exceeded its {timeout.TotalSeconds:0} s timeout" : "was cancelled by the client");
            KillTree(process);
            if (!timedOut) throw;
        }

        await pumps.ConfigureAwait(false);
        stopwatch.Stop();

        var exitCode = process.HasExited ? process.ExitCode : -1;
        var (meaning, success) = timedOut
            ? ($"Timed out after {timeout.TotalSeconds:0} s and was terminated.", false)
            : InstallerRunResult.Interpret(exitCode);

        JsonElement? json = null;
        string? text = stdout.Text;
        if (invocation.JsonRequested && TryParseJson(text, invocation.Verb!.Id, out var element))
        {
            json = element;
            text = null;
        }

        return new InstallerRunResult
        {
            Command = invocation.Verb!.Id,
            Arguments = invocation.Arguments,
            Ran = true,
            ExitCode = timedOut ? null : exitCode,
            ExitMeaning = meaning,
            Succeeded = success && !timedOut,
            TimedOut = timedOut,
            OutputTruncated = stdout.Truncated || stderr.Truncated,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Json = json,
            Stdout = string.IsNullOrWhiteSpace(text) ? null : text,
            Stderr = string.IsNullOrWhiteSpace(stderr.Text) ? null : stderr.Text,
        };
    }

    private bool TryParseJson(string text, string command, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var document = JsonDocument.Parse(text, JsonParse);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            // Not JSON after all (a human-readable error went to stdout): the caller gets the raw text
            // in Stdout instead, which is the honest outcome — nothing is lost.
            _logger.LogInformation(ex, "'{Command}' was asked for /JSON but printed text; returning it as stdout.", command);
            return false;
        }
    }

    private void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException ex)
        {
            // Exited between the check and the kill: nothing left to terminate. Reported, not hidden.
            _logger.LogInformation(ex, "Process exited before it could be terminated.");
        }
    }

    /// <summary>Reads a stream to the end but keeps only the first <see cref="MaxCapturedChars"/>, so a chatty command cannot exhaust memory.</summary>
    private sealed class BoundedCapture
    {
        private readonly StringBuilder _text = new();

        public bool Truncated { get; private set; }
        public string Text => _text.ToString();

        public async Task PumpAsync(StreamReader reader)
        {
            var buffer = new char[8192];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                var room = MaxCapturedChars - _text.Length;
                if (room <= 0) { Truncated = true; continue; }
                var take = Math.Min(room, read);
                _text.Append(buffer, 0, take);
                if (take < read) Truncated = true;
            }
        }
    }
}
