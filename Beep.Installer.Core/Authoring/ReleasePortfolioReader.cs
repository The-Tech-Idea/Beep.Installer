using System.Text.Json;
using Beep.Installer.Quality;

namespace Beep.Installer.Engine;

public enum ReleasePortfolioLoadError { None, Unavailable, Invalid, TooLarge }
public sealed record ReleasePortfolioLoadResult(ReleaseQualificationPortfolioReport? Report, ReleasePortfolioLoadError Error);

public static class ReleasePortfolioReader
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static ReleasePortfolioLoadResult Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumBytes) return new(null, ReleasePortfolioLoadError.TooLarge);
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            bool Has(string name, JsonValueKind kind) => root.ValueKind == JsonValueKind.Object && root.EnumerateObject()
                .Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == kind);
            if (!Has("Evidence", JsonValueKind.Array) || !Has("EvidenceRoot", JsonValueKind.String)
                || (!Has("Success", JsonValueKind.True) && !Has("Success", JsonValueKind.False)) || !Has("Diagnostics", JsonValueKind.Array)
                || !Has("CompletedUtc", JsonValueKind.String) || root.EnumerateObject().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                return new(null, ReleasePortfolioLoadError.Invalid);
            var evidenceJson = root.EnumerateObject().Single(property => property.Name.Equals("Evidence", StringComparison.OrdinalIgnoreCase)).Value;
            foreach (var item in evidenceJson.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return new(null, ReleasePortfolioLoadError.Invalid);
                var properties = item.EnumerateObject().ToArray();
                bool Field(string name, params JsonValueKind[] kinds) => properties.Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && kinds.Contains(property.Value.ValueKind));
                if (properties.GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)
                    || !Field("Diagnostics", JsonValueKind.Array) || !Field("Found", JsonValueKind.True, JsonValueKind.False)
                    || !Field("Success", JsonValueKind.True, JsonValueKind.False, JsonValueKind.Null))
                    return new(null, ReleasePortfolioLoadError.Invalid);
            }
            var report = JsonSerializer.Deserialize<ReleaseQualificationPortfolioReport>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (report?.Evidence is null || report.Diagnostics is null || report.Diagnostics.Any(d => d is null || !Enum.IsDefined(d.Severity))
                || report.Evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.QualificationId) || item.Diagnostics is null || item.Diagnostics.Any(d => d is null || !Enum.IsDefined(d.Severity))))
                return new(null, ReleasePortfolioLoadError.Invalid);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            // Keep only the fields used by the overview; raw messages/audit details are not retained.
            var selected = new ReleaseQualificationPortfolioReport
            {
                CompletedUtc = report.CompletedUtc,
                Success = report.Success && !report.Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error),
                Evidence = report.Evidence.Select(item => new ReleaseQualificationEvidence
                {
                    QualificationId = item.QualificationId,
                    ReportPath = string.IsNullOrWhiteSpace(item.ReportPath) ? "" : Path.GetFullPath(item.ReportPath, directory),
                    Found = item.Found,
                    Success = item.Success == true && item.Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error),
                    CompletedUtc = item.CompletedUtc
                }).ToList()
            };
            return new(selected, ReleasePortfolioLoadError.None);
        }
        catch (JsonException) { return new(null, ReleasePortfolioLoadError.Invalid); }
        catch (ArgumentException) { return new(null, ReleasePortfolioLoadError.Invalid); }
        catch (NotSupportedException) { return new(null, ReleasePortfolioLoadError.Invalid); }
        catch (IOException) { return new(null, ReleasePortfolioLoadError.Unavailable); }
        catch (UnauthorizedAccessException) { return new(null, ReleasePortfolioLoadError.Unavailable); }
    }
}
