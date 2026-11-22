using Beep.Installer.Engine;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Installer;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Ui;

/// <summary>Explains captured rule syntax without evaluating any condition.</summary>
internal static class ConditionPreviewPresentation
{
    internal static string Render(InstallComponent component, IReadOnlyList<ConditionListValidator.Issue> issues)
    {
        if (component.Conditions is null || component.Conditions.Count == 0)
            return L("Conditions_PreviewEmpty", "No component gates. This component is available whenever its install type/profile allows it.");
        var lines = new List<string>
        {
            $"{L("Conditions_GroupExpression", "Group expression:")} {component.ConditionExpression}",
            component.ConditionExpression switch
            {
                ConditionExpressionMode.Any => L("Conditions_PreviewAny", "Meaning: install/offer this component when at least one rule passes."),
                ConditionExpressionMode.Not => L("Conditions_PreviewNot", "Meaning: install/offer this component when the complete rule group does not pass."),
                _ => L("Conditions_PreviewAll", "Meaning: install/offer this component only when every rule passes.")
            },
            ""
        };
        for (var i = 0; i < component.Conditions.Count; i++)
            lines.Add($"{(i + 1).ToString(LanguageManager.CurrentCulture)}. {Describe(component.Conditions[i])}");
        if (issues.Count > 0)
        {
            lines.Add(""); lines.Add(L("Conditions_PreviewIssues", "Issues:"));
            foreach (var issue in issues)
                lines.Add($"- {AuthoringReportPresentation.Status(issue.Severity.ToString())} #{(issue.Index + 1).ToString(LanguageManager.CurrentCulture)}: {issue.Message}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Describe(InstallCondition? condition)
    {
        if (condition is null) return L("Conditions_PreviewNull", "Missing rule — remove this row or add a rule.");
        var op = string.IsNullOrWhiteSpace(condition.Operator) ? "==" : condition.Operator.Trim();
        var format = condition.Type switch
        {
            ConditionType.AlwaysTrue => L("Conditions_PreviewAlwaysTrue", "Always true"),
            ConditionType.AlwaysFalse => L("Conditions_PreviewAlwaysFalse", "Always false"),
            ConditionType.IsAdmin => L("Conditions_PreviewAdmin", "Current installer session is elevated/admin"),
            ConditionType.Architecture => L("Conditions_PreviewArchitecture", "Machine architecture {0} {1}"),
            ConditionType.OsVersion => L("Conditions_PreviewOsVersion", "Windows version {0} {1}"),
            ConditionType.FileExists => L("Conditions_PreviewFileExists", "File exists: {1}"),
            ConditionType.DirectoryExists => L("Conditions_PreviewDirectoryExists", "Directory exists: {1}"),
            ConditionType.RegistryExists => L("Conditions_PreviewRegistryExists", "Registry key exists: {1}"),
            ConditionType.RegistryValue => L("Conditions_PreviewRegistryValue", "Registry value {1} {0} {2}"),
            ConditionType.CommandReturns => L("Conditions_PreviewCommandReturns", "Command `{1}` output/exit {0} {2}"),
            _ => null
        };
        // Authored strings are arguments, never formats or translated resource keys.
        return format is null ? $"{condition.Type} {op} {condition.Value}" :
            string.Format(LanguageManager.CurrentCulture, format, op, condition.Value, condition.Value2);
    }
}
