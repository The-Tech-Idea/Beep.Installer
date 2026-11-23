using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Models;

namespace Beep.Installer.Ui;

/// <summary>Captures every authored field without normalizing or sharing the editable graph.</summary>
internal sealed class ProjectReviewSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };
    private readonly InstallProject _origin;
    private readonly string _hash;
    internal InstallProject Candidate { get; }
    internal InstallProject Summary { get; }

    private ProjectReviewSnapshot(InstallProject origin, string json)
    {
        _origin = origin; _hash = Hash(json);
        Candidate = JsonSerializer.Deserialize<InstallProject>(json, Options)
            ?? throw new InvalidOperationException("The project could not be captured for review.");
        Summary = JsonSerializer.Deserialize<InstallProject>(json, Options)
            ?? throw new InvalidOperationException("The project summary could not be captured.");
    }

    internal static ProjectReviewSnapshot Capture(InstallProject project)
        => new(project, JsonSerializer.Serialize(project, Options));

    internal bool Matches(InstallProject project)
        => ReferenceEquals(_origin, project) && _hash == Hash(JsonSerializer.Serialize(project, Options));

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
