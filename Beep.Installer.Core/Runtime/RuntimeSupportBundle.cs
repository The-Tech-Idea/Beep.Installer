using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beep.Installer.Policy;
using Beep.Installer.Models;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Engine;

public sealed class RuntimeSupportBundleOptions
{
    public const int DefaultRetentionDays = 30;
    public const int MaximumRetentionDays = 3650;

    public string Action { get; init; } = "";
    public InstallProject Project { get; init; } = new();
    public string InstallPath { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string LogPath { get; init; } = "";
    public SetupContext? Context { get; init; }
    public InstallerPolicyEvaluation? PolicyEvaluation { get; init; }
    public string? OutputPath { get; init; }
    public string? CorrelationId { get; init; }
    public bool PreviewOnly { get; init; }
    public bool ConsentGranted { get; init; } = true;
    public int RetentionDays { get; init; } = DefaultRetentionDays;
}

public sealed class RuntimeSupportBundleResult
{
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool PreviewOnly { get; init; }
    public bool ConsentGranted { get; init; }
    public int RetentionDays { get; init; }
    public int RetentionDeletedCount { get; init; }
}

/// <summary>
/// Generates the canonical runtime support artifact for silent install, repair and uninstall.
/// The artifact is deliberately JSON so customers can preview, diff and redact it before sharing.
/// </summary>
public sealed class RuntimeSupportBundleGenerator
{
    private const int MaxLogChars = 128 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RuntimeSupportBundleResult Generate(RuntimeSupportBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Project);

        var outputPath = ResolveOutputPath(options);
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var retentionDays = NormalizeRetentionDays(options.RetentionDays);
        var retention = PruneExpiredBundles(outputDirectory, retentionDays, outputPath);
        var generatedAtUtc = DateTimeOffset.UtcNow;

        var planResult = new InstallPlanCompiler().Compile(options.Project, options.PolicyEvaluation);
        var redactedPlan = planResult.Plan is null
            ? null
            : RedactJsonDocument(InstallPlanCompiler.ToJson(planResult.Plan));

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "1.0",
            ["generatedAtUtc"] = generatedAtUtc,
            ["correlationId"] = string.IsNullOrWhiteSpace(options.CorrelationId) ? StableCorrelationId(options) : options.CorrelationId,
            ["action"] = options.Action,
            ["success"] = options.Success,
            ["exitCode"] = options.ExitCode,
            ["message"] = RuntimeRedactor.Redact(options.Message),
            ["privacy"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["previewOnly"] = options.PreviewOnly,
                ["consentGranted"] = options.ConsentGranted && !options.PreviewOnly,
                ["redactionApplied"] = true,
                ["sharingRequiresReview"] = true,
                ["sensitiveValuePolicy"] = "redact-known-secret-keys-and-local-user-paths"
            },
            ["retention"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["enabled"] = retentionDays > 0,
                ["retentionDays"] = retentionDays,
                ["cutoffUtc"] = retention.CutoffUtc,
                ["deletedExpiredBundleCount"] = retention.DeletedCount,
                ["directory"] = RuntimeRedactor.RedactPath(outputDirectory),
                ["matchPattern"] = "*-support-bundle.json"
            },
            ["product"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = options.Project.AppName ?? "",
                ["version"] = options.Project.AppVersion ?? "",
                ["publisher"] = options.Project.AppPublisher ?? "",
                ["outputFormat"] = options.Project.OutputFormat.ToString(),
                ["scope"] = options.Project.DefaultScope.ToString()
            },
            ["paths"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["installPath"] = RuntimeRedactor.RedactPath(options.InstallPath),
                ["logPath"] = RuntimeRedactor.RedactPath(options.LogPath),
                ["diagnosticJsonLogPath"] = RuntimeRedactor.RedactPath(Diag.JsonLogPath),
                ["journalPath"] = RuntimeRedactor.RedactPath(options.Context?.TryGetProperty<string>(InstallContextKeys.ResourceExecutionJournalPath) ?? "")
            },
            ["system"] = SystemFacts(),
            ["policy"] = options.PolicyEvaluation is null ? null : InstallerPolicyEvaluator.CreateEvidence(options.PolicyEvaluation),
            ["plan"] = redactedPlan,
            ["diagnostics"] = Diag.Recent.Select(entry => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["time"] = entry.Time,
                ["eventId"] = entry.EventId,
                ["level"] = entry.Level,
                ["operation"] = RuntimeRedactor.Redact(entry.Operation),
                ["correlationId"] = RuntimeRedactor.Redact(entry.CorrelationId),
                ["context"] = RuntimeRedactor.Redact(entry.Context),
                ["message"] = RuntimeRedactor.Redact(entry.Message),
                ["errorType"] = RuntimeRedactor.Redact(entry.ErrorType),
                ["error"] = RuntimeRedactor.Redact(entry.Error ?? "")
            }).ToList(),
            ["logs"] = LogArtifacts(options.LogPath),
            ["journal"] = FileArtifact(options.Context?.TryGetProperty<string>(InstallContextKeys.ResourceExecutionJournalPath) ?? "", includeContent: false)
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));

        return new RuntimeSupportBundleResult
        {
            Path = outputPath,
            Sha256 = Sha256File(outputPath),
            SizeBytes = new FileInfo(outputPath).Length,
            PreviewOnly = options.PreviewOnly,
            ConsentGranted = options.ConsentGranted && !options.PreviewOnly,
            RetentionDays = retentionDays,
            RetentionDeletedCount = retention.DeletedCount
        };
    }

    public static string ResolveOutputPath(RuntimeSupportBundleOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var full = Path.GetFullPath(options.OutputPath!);
            if (Path.HasExtension(full))
                return full;

            return Path.Combine(full, DefaultFileName(options));
        }

        var baseDirectory = !string.IsNullOrWhiteSpace(options.InstallPath) && Directory.Exists(options.InstallPath)
            ? options.InstallPath
            : Path.GetTempPath();
        return Path.Combine(Path.GetFullPath(baseDirectory), "support", DefaultFileName(options));
    }

    private static string DefaultFileName(RuntimeSupportBundleOptions options)
        => $"{SafeSegment(options.Project.AppName)}-{SafeSegment(options.Action)}-support-bundle.json";

    private static int NormalizeRetentionDays(int value)
    {
        if (value < 0)
            return RuntimeSupportBundleOptions.DefaultRetentionDays;
        if (value > RuntimeSupportBundleOptions.MaximumRetentionDays)
            return RuntimeSupportBundleOptions.MaximumRetentionDays;
        return value;
    }

    private static (int DeletedCount, DateTimeOffset? CutoffUtc) PruneExpiredBundles(string directory, int retentionDays, string currentOutputPath)
    {
        if (retentionDays <= 0)
            return (0, null);

        try
        {
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(fullDirectory))
                return (0, DateTimeOffset.UtcNow.AddDays(-retentionDays));

            var currentFullPath = Path.GetFullPath(currentOutputPath);
            var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            var deleted = 0;

            foreach (var candidate in Directory.EnumerateFiles(fullDirectory, "*-support-bundle.json", SearchOption.TopDirectoryOnly))
            {
                var fullCandidate = Path.GetFullPath(candidate);
                if (!IsImmediateChild(fullDirectory, fullCandidate))
                    continue;
                if (string.Equals(fullCandidate, currentFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = new FileInfo(fullCandidate);
                if (info.LastWriteTimeUtc >= cutoffUtc.UtcDateTime)
                    continue;

                File.Delete(fullCandidate);
                deleted++;
            }

            return (deleted, cutoffUtc);
        }
        catch
        {
            return (0, DateTimeOffset.UtcNow.AddDays(-retentionDays));
        }
    }

    private static bool IsImmediateChild(string fullDirectory, string fullCandidate)
    {
        var parent = Path.GetDirectoryName(fullCandidate)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(parent, fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeSegment(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (string.IsNullOrWhiteSpace(value) ? "installer" : value!)
            .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "installer" : result;
    }

    private static string StableCorrelationId(RuntimeSupportBundleOptions options)
    {
        var seed = $"{options.Action}|{options.Project.AppName}|{options.Project.AppVersion}|{options.InstallPath}|{options.ExitCode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..16].ToLowerInvariant();
    }

    private static SortedDictionary<string, object?> SystemFacts()
        => new(StringComparer.Ordinal)
        {
            ["osDescription"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
            ["installerAssemblyVersion"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "",
            ["is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem,
            ["is64BitProcess"] = Environment.Is64BitProcess,
            ["processorCount"] = Environment.ProcessorCount
        };

    private static List<SortedDictionary<string, object?>> LogArtifacts(string logPath)
    {
        var logs = new List<SortedDictionary<string, object?>>();
        AddIfPresent(logs, logPath, includeContent: true);
        AddIfPresent(logs, Diag.LogPath, includeContent: true);
        AddIfPresent(logs, Diag.JsonLogPath, includeContent: true);
        return logs
            .GroupBy(log => log.TryGetValue("sha256", out var sha) ? sha as string ?? "" : "")
            .Select(group => group.First())
            .ToList();
    }

    private static void AddIfPresent(List<SortedDictionary<string, object?>> artifacts, string path, bool includeContent)
    {
        var artifact = FileArtifact(path, includeContent);
        if (artifact != null)
            artifacts.Add(artifact);
    }

    private static SortedDictionary<string, object?>? FileArtifact(string path, bool includeContent)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["path"] = RuntimeRedactor.RedactPath(fullPath),
                    ["exists"] = false
                };
            }

            var info = new FileInfo(fullPath);
            var artifact = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = RuntimeRedactor.RedactPath(fullPath),
                ["name"] = info.Name,
                ["exists"] = true,
                ["sizeBytes"] = info.Length,
                ["sha256"] = Sha256File(fullPath),
                ["lastWriteTimeUtc"] = info.LastWriteTimeUtc
            };

            if (includeContent)
            {
                var text = File.ReadAllText(fullPath, Encoding.UTF8);
                artifact["truncated"] = text.Length > MaxLogChars;
                artifact["content"] = RuntimeRedactor.Redact(text.Length > MaxLogChars ? text[^MaxLogChars..] : text);
            }

            return artifact;
        }
        catch (Exception ex)
        {
            return new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = RuntimeRedactor.RedactPath(path),
                ["exists"] = false,
                ["error"] = RuntimeRedactor.Redact(ex.Message)
            };
        }
    }

    private static object? RedactJsonDocument(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RedactJsonElement(document.RootElement);
    }

    private static object? RedactJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => SensitiveName(property.Name)
                    ? "***REDACTED***"
                    : RedactJsonElement(property.Value),
                StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactJsonElement).ToList(),
            JsonValueKind.String => RuntimeRedactor.Redact(element.GetString() ?? ""),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool SensitiveName(string name)
        => name.Contains("password", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || name.Contains("token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
           || name.Contains("accessKey", StringComparison.OrdinalIgnoreCase)
           || name.Contains("privateKey", StringComparison.OrdinalIgnoreCase)
           || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
           || name.Contains("connectionString", StringComparison.OrdinalIgnoreCase);

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

}

public static class RuntimeRedactor
{
    private static readonly Regex SecretAssignment = new(
        "(?i)([\\w.-]*(?:password|secret|token|apikey|api_key|accesskey|access_key|connectionstring|connection_string)[\\w.-]*)\\s*[:=]\\s*([^\\s,;]+)",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var redacted = SecretAssignment.Replace(value, "$1=***REDACTED***");
        return RedactPath(redacted);
    }

    public static string RedactPath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var result = value;
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(temp))
            result = result.Replace(temp, "%TEMP%", StringComparison.OrdinalIgnoreCase);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            result = result.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
