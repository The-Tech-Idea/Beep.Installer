using System.Collections.Generic;
using System.Linq;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>
/// Validates a list of <see cref="InstallCondition"/> entries (Track A3.4) so the editor
/// dialog can surface obvious problems before they reach the install-time evaluator. Pure so it
/// is unit-testable; the actual evaluation is done by
/// <c>TheTechIdea.Beep.Installer.InstallConditionEvaluator</c> at install time.
/// </summary>
public static class ConditionListValidator
{
    public class Issue
    {
        public int Index { get; set; }
        public string Message { get; set; } = "";
        public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
    }
    public enum IssueSeverity { Info, Warning, Error }

    public static IReadOnlyList<Issue> Validate(IList<InstallCondition> conditions)
    {
        var issues = new List<Issue>();
        if (conditions == null) return issues;

        for (int i = 0; i < conditions.Count; i++)
        {
            var c = conditions[i];
            if (c == null) { issues.Add(new Issue { Index = i, Message = "Condition is null.", Severity = IssueSeverity.Error }); continue; }

            switch (c.Type)
            {
                case ConditionType.OsVersion:
                case ConditionType.Architecture:
                case ConditionType.RegistryValue:
                case ConditionType.CommandReturns:
                case ConditionType.FileExists:
                case ConditionType.DirectoryExists:
                case ConditionType.RegistryExists:
                    if (string.IsNullOrWhiteSpace(c.Value))
                        issues.Add(new Issue { Index = i, Message = $"Value is required for {c.Type}.", Severity = IssueSeverity.Error });
                    break;
                case ConditionType.AlwaysTrue:
                case ConditionType.AlwaysFalse:
                case ConditionType.IsAdmin:
                    // No required fields.
                    break;
            }
            if (c.Type == ConditionType.RegistryValue && string.IsNullOrWhiteSpace(c.Value2))
            {
                issues.Add(new Issue { Index = i, Message = "Value2 is required for RegistryValue (the expected value to compare against).", Severity = IssueSeverity.Warning });
            }
            if (!string.IsNullOrEmpty(c.Operator))
            {
                var op = c.Operator.Trim();
                var allowed = new[] { "==", "=", "!=", ">", ">=", "<", "<=" };
                if (!allowed.Contains(op))
                    issues.Add(new Issue { Index = i, Message = $"Operator '{op}' is not a recognized comparison operator.", Severity = IssueSeverity.Warning });
            }
        }
        return issues;
    }
}
