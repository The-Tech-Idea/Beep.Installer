using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Engine;

public sealed class RuntimeTelemetryOptions
{
    public string Action { get; init; } = "";
    public InstallProject Project { get; init; } = new();
    public string InstallPath { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string LogPath { get; init; } = "";
    public SetupContext? Context { get; init; }
    public InstallerPolicyEvaluation? PolicyEvaluation { get; init; }
    public string CorrelationId { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public Func<RuntimeTelemetryEnvelope, RuntimeTelemetrySendResult>? Sender { get; init; }
}

public sealed class RuntimeTelemetryResult
{
    public string Mode { get; init; } = "disabled";
    public bool Emitted { get; init; }
    public bool LocalWritten { get; init; }
    public bool RemoteSent { get; init; }
    public string LocalPath { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class RuntimeTelemetryEnvelope
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset EmittedAtUtc { get; init; }
    public string Mode { get; init; } = "";
    public string Action { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string ProductPublisher { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public string LogPath { get; init; } = "";
    public string JournalPath { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string PolicyStatus { get; init; } = "";
    public string EffectivePolicySha256 { get; init; } = "";
    public List<RuntimeTelemetryDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class RuntimeTelemetryDiagnostic
{
    public DateTimeOffset Time { get; init; }
    public string EventId { get; init; } = "";
    public string Level { get; init; } = "";
    public string Operation { get; init; } = "";
    public string Context { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class RuntimeTelemetrySendResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

public sealed class RuntimeTelemetrySink
{
    public RuntimeTelemetryResult Emit(RuntimeTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Project);

        var policy = options.PolicyEvaluation?.Policy;
        var mode = NormalizeMode(policy?.TelemetryMode);
        if (mode == "disabled")
            return new RuntimeTelemetryResult { Mode = mode, Message = "Telemetry disabled by policy." };

        var envelope = BuildEnvelope(options, mode);
        var result = new RuntimeTelemetryResult
        {
            Mode = mode,
            Emitted = true,
            Endpoint = policy?.TelemetryEndpoint ?? ""
        };

        if (mode is "local" or "anonymous" or "full")
        {
            var localPath = ResolveLocalPath(options, mode);
            WriteLocal(localPath, envelope);
            result = result.WithLocal(localPath);
        }

        if (mode is "anonymous" or "full")
        {
            var endpoint = policy?.TelemetryEndpoint ?? "";
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                var send = options.Sender?.Invoke(envelope) ?? SendHttp(endpoint, envelope);
                result = result.WithRemote(send.Success, send.Message);
            }
        }

        return result;
    }

    private static RuntimeTelemetryEnvelope BuildEnvelope(RuntimeTelemetryOptions options, string mode)
    {
        var policyEvidence = options.PolicyEvaluation is null ? null : InstallerPolicyEvaluator.CreateEvidence(options.PolicyEvaluation);
        var includeIdentity = mode == "full";
        var includePaths = mode == "full";
        return new RuntimeTelemetryEnvelope
        {
            EmittedAtUtc = DateTimeOffset.UtcNow,
            Mode = mode,
            Action = RuntimeRedactor.Redact(options.Action),
            CorrelationId = RuntimeRedactor.Redact(options.CorrelationId),
            Success = options.Success,
            ExitCode = options.ExitCode,
            Message = RuntimeRedactor.Redact(options.Message),
            ProductName = includeIdentity ? RuntimeRedactor.Redact(options.Project.AppName) : "",
            ProductVersion = includeIdentity ? RuntimeRedactor.Redact(options.Project.AppVersion) : "",
            ProductPublisher = includeIdentity ? RuntimeRedactor.Redact(options.Project.AppPublisher) : "",
            InstallPath = includePaths ? RuntimeRedactor.RedactPath(options.InstallPath) : "",
            LogPath = includePaths ? RuntimeRedactor.RedactPath(options.LogPath) : "",
            JournalPath = includePaths ? RuntimeRedactor.RedactPath(options.Context?.TryGetProperty<string>(InstallContextKeys.ResourceExecutionJournalPath) ?? "") : "",
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            PolicyStatus = policyEvidence?.Status ?? "not-configured",
            EffectivePolicySha256 = policyEvidence?.EffectivePolicySha256 ?? "",
            Diagnostics = Diag.Recent
                .Where(entry => entry.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase) || entry.Level.Equals("INFO", StringComparison.OrdinalIgnoreCase))
                .TakeLast(50)
                .Select(entry => new RuntimeTelemetryDiagnostic
                {
                    Time = entry.Time,
                    EventId = entry.EventId,
                    Level = entry.Level,
                    Operation = RuntimeRedactor.Redact(entry.Operation),
                    Context = RuntimeRedactor.Redact(entry.Context),
                    Message = RuntimeRedactor.Redact(entry.Message)
                })
                .ToList()
        };
    }

    private static void WriteLocal(string path, RuntimeTelemetryEnvelope envelope)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(
            path,
            JsonSerializer.Serialize(envelope, RuntimeTelemetryJsonContext.Default.RuntimeTelemetryEnvelope) + Environment.NewLine,
            new UTF8Encoding(false));
    }

    private static RuntimeTelemetrySendResult SendHttp(string endpoint, RuntimeTelemetryEnvelope envelope)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var content = new StringContent(
                JsonSerializer.Serialize(envelope, RuntimeTelemetryJsonContext.Default.RuntimeTelemetryEnvelope),
                Encoding.UTF8,
                "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            using var response = client.Send(request);
            return new RuntimeTelemetrySendResult
            {
                Success = response.IsSuccessStatusCode,
                Message = $"{(int)response.StatusCode} {response.ReasonPhrase}"
            };
        }
        catch (Exception ex)
        {
            return new RuntimeTelemetrySendResult { Success = false, Message = ex.Message };
        }
    }

    private static string ResolveLocalPath(RuntimeTelemetryOptions options, string mode)
        => string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine(Path.GetTempPath(), "BeepTelemetry", $"runtime-{mode}.jsonl")
            : Path.GetFullPath(options.OutputPath);

    private static string NormalizeMode(string? mode)
        => string.IsNullOrWhiteSpace(mode) ? "disabled" : mode.Trim().ToLowerInvariant();
}

internal static class RuntimeTelemetryResultExtensions
{
    public static RuntimeTelemetryResult WithLocal(this RuntimeTelemetryResult result, string localPath)
        => new()
        {
            Mode = result.Mode,
            Emitted = result.Emitted,
            LocalWritten = true,
            RemoteSent = result.RemoteSent,
            LocalPath = localPath,
            Endpoint = result.Endpoint,
            Message = result.Message
        };

    public static RuntimeTelemetryResult WithRemote(this RuntimeTelemetryResult result, bool remoteSent, string message)
        => new()
        {
            Mode = result.Mode,
            Emitted = result.Emitted,
            LocalWritten = result.LocalWritten,
            RemoteSent = remoteSent,
            LocalPath = result.LocalPath,
            Endpoint = result.Endpoint,
            Message = message
        };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimeTelemetryEnvelope))]
[JsonSerializable(typeof(RuntimeTelemetryDiagnostic))]
internal sealed partial class RuntimeTelemetryJsonContext : JsonSerializerContext
{
}
