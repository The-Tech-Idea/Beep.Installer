using System.Globalization;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Lang;

namespace Beep.Installer.Ui;

internal static class AuthoringReportPresentation
{
    internal static T Capture<T>(T report) where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(report)) ?? throw new InvalidOperationException("The report could not be captured.");
    private static string L(string key, string fallback) => LanguageManager.GetOrDefault(key, fallback);
    private static string F(string key, string fallback, params object[] values) => string.Format(LanguageManager.CurrentCulture, L(key, fallback), values);
    internal static string Status(string value) => value.ToLowerInvariant() switch
    {
        "ready" => L("Report_Ready", "Ready"), "ready with warnings" => L("Report_ReadyWithWarnings", "Ready with warnings"),
        "blocked" => L("Report_Blocked", "Blocked"), "error" => L("Common_Error", "Error"),
        "warning" => L("Report_Warning", "Warning"), "info" => L("Common_Info", "Info"), _ => value
    };
    private static string Area(ProjectValidationArea area)
    {
        var key = area.Id switch
        {
            "project" => "Nav_Project", "components" => "Nav_Components", "prerequisites" => "Nav_Prerequisites", "deployment" => "Section_Supersedence",
            "services" => "Section_WindowsServices", "scheduledtasks" => "Section_ScheduledTasks", "firewallrules" => "Section_FirewallRules",
            "fileassociations" => "Section_FileAssociations", "certificates" => "Section_Certificates", "comregistrations" => "Section_ComRegistrations",
            "driverpackages" => "Section_DriverPackages", "configtransforms" => "Section_ConfigTransforms", "iisapppools" => "Section_IisAppPools",
            "iissites" => "Section_IisSites", "webdeploy" => "Section_WebDeploy", _ => ""
        };
        return key.Length == 0 ? area.Label : L(key, area.Label);
    }
    internal static string Validation(string name, ProjectValidationCenterReport report)
    {
        using var writer = new StringWriter(LanguageManager.CurrentCulture);
        writer.WriteLine($"{L("Builder_ValidationTitle", "Validation center")}: {name}");
        writer.WriteLine($"{L("Report_Schema", "Schema")}: {report.SchemaVersion}");
        writer.WriteLine($"{L("Report_PlanHash", "Plan hash")}: {report.PlanHash}");
        writer.WriteLine(F("Report_DiagnosticCounts", "Errors: {0:N0}, warnings: {1:N0}", report.ErrorCount, report.WarningCount)); writer.WriteLine();
        if (report.Areas.Count == 0) writer.WriteLine(L("Report_Clean", "No validation findings under strict authoring validation."));
        foreach (var area in report.Areas)
        {
            writer.WriteLine($"{Area(area)} — {F("Report_DiagnosticCounts", "Errors: {0:N0}, warnings: {1:N0}", area.ErrorCount, area.WarningCount)}");
            foreach (var finding in area.Findings)
            {
                writer.WriteLine($"  [{Status(finding.Severity)}] {finding.Code} {finding.Path}"); writer.WriteLine($"      {finding.Message}");
                if (!string.IsNullOrWhiteSpace(finding.Fix)) writer.WriteLine($"      {L("Report_Fix", "Fix")}: {finding.Fix}");
            }
            writer.WriteLine();
        }
        return writer.ToString();
    }
    internal static string Capabilities(string name, PackageFormatCapabilityReport report)
    {
        using var writer = new StringWriter(LanguageManager.CurrentCulture);
        writer.WriteLine($"{L("Builder_FormatReadinessTitle", "Format readiness")}: {name}");
        writer.WriteLine($"{L("Report_PlanHash", "Plan hash")}: {report.PlanHash}");
        writer.WriteLine($"{L("Report_ReleaseReadiness", "Release readiness")}: {Status(report.ReleaseReadinessStatus)}");
        writer.WriteLine(F("Report_FormatCounts", "Ready: {0:N0}, warnings: {1:N0}, blocked: {2:N0}", report.ReadyFormatCount, report.WarningFormatCount, report.BlockedFormatCount));
        writer.WriteLine(report.ReleaseReadinessSummary); writer.WriteLine();
        foreach (var format in report.Formats)
        {
            writer.WriteLine($"{format.Format}: {Status(format.Status)}"); writer.WriteLine($"  {format.Summary}");
            if (format.CanonicalArtifacts.Count > 0) writer.WriteLine($"  {L("Report_Artifacts", "Artifacts")}: {string.Join(", ", format.CanonicalArtifacts)}");
            foreach (var finding in format.Findings.Take(12))
            {
                var code = string.IsNullOrWhiteSpace(finding.Code) ? L("Report_Finding", "Finding") : finding.Code;
                writer.WriteLine($"  [{Status(finding.Severity)}] {code}: {finding.Message}");
            }
            if (format.Findings.Count > 12) writer.WriteLine("  ... " + F("Report_MoreFindings", "{0:N0} more finding(s)", format.Findings.Count - 12));
            writer.WriteLine();
        }
        return writer.ToString();
    }

    internal static void UpdateText(TextBox box, string text)
    {
        if (box.Text == text) return;
        var start = box.SelectionStart; var length = box.SelectionLength; var selected = box.SelectedText;
        var match = selected.Length == 0 ? -1 : text.IndexOf(selected, StringComparison.Ordinal);
        if (match >= 0 && text.IndexOf(selected, match + selected.Length, StringComparison.Ordinal) < 0) start = match;
        box.Text = text; start = Math.Min(start, box.TextLength);
        box.Select(start, Math.Min(length, box.TextLength - start));
    }
}
