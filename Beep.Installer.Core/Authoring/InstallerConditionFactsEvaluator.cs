using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

public sealed class InstallerConditionEvaluationContext
{
    public IInstallerConditionFacts ConditionFacts { get; init; } = SystemInstallerConditionFacts.Instance;
    public string InstallRoot { get; init; } = "";
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public static class InstallerConditionFactsEvaluator
{
    public static bool Evaluate(
        IReadOnlyList<InstallCondition>? conditions,
        ConditionExpressionMode expression,
        InstallerConditionEvaluationContext? context = null)
    {
        if (conditions is null || conditions.Count == 0)
            return true;

        context ??= new InstallerConditionEvaluationContext();
        var results = conditions.Select(condition => Evaluate(condition, context)).ToList();
        return expression switch
        {
            ConditionExpressionMode.Any => results.Any(result => result),
            ConditionExpressionMode.Not => !results.All(result => result),
            _ => results.All(result => result)
        };
    }

    public static bool Evaluate(
        InstallCondition condition,
        InstallerConditionEvaluationContext? context = null)
    {
        context ??= new InstallerConditionEvaluationContext();
        var facts = context.ConditionFacts;
        var value = Expand(condition.Value ?? "", context);
        var value2 = Expand(condition.Value2 ?? "", context);
        var op = condition.Operator ?? "";

        return condition.Type switch
        {
            ConditionType.AlwaysTrue => true,
            ConditionType.AlwaysFalse => false,
            ConditionType.OsVersion => Compare(facts.OsVersion, ParseVersionOrDefault(value, facts.OsVersion), op),
            ConditionType.Architecture => string.IsNullOrWhiteSpace(value)
                                          || string.Equals(facts.Architecture, value, StringComparison.OrdinalIgnoreCase),
            ConditionType.FileExists => facts.FileExists(value),
            ConditionType.DirectoryExists => facts.DirectoryExists(value),
            ConditionType.RegistryExists => facts.RegistryKeyExists(value),
            ConditionType.RegistryValue => CheckRegistryValue(facts, value, op, value2),
            ConditionType.CommandReturns => CheckCommand(facts, value, op, value2),
            ConditionType.IsAdmin => facts.IsAdmin,
            _ => true
        };
    }

    public static bool EvaluateCompiledOperation(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var count = IntInput(operation, "conditionCount");
        if (count == 0 && operation.Condition.Contains("AlwaysFalse", StringComparison.OrdinalIgnoreCase))
            return false;
        if (count == 0)
            return true;

        var expression = ParseExpression(Input(operation, "conditionExpression"));
        var conditions = new List<InstallCondition>(count);
        for (var i = 0; i < count; i++)
        {
            var prefix = $"condition.{i}.";
            if (!Enum.TryParse<ConditionType>(Input(operation, prefix + "type"), ignoreCase: true, out var type))
                type = ConditionType.AlwaysTrue;
            conditions.Add(new InstallCondition
            {
                Type = type,
                Value = Input(operation, prefix + "value"),
                Value2 = Input(operation, prefix + "value2"),
                Operator = Input(operation, prefix + "operator")
            });
        }

        return Evaluate(conditions, expression, new InstallerConditionEvaluationContext
        {
            ConditionFacts = context.ConditionFacts ?? SystemInstallerConditionFacts.Instance,
            InstallRoot = context.InstallRoot,
            Variables = context.Variables
        });
    }

    private static bool CheckRegistryValue(IInstallerConditionFacts facts, string keyValuePath, string op, string expected)
    {
        if (string.IsNullOrWhiteSpace(keyValuePath))
            return false;

        var parts = keyValuePath.Split('|', 2);
        var actual = facts.RegistryValue(parts[0], parts.Length > 1 ? parts[1] : "");
        return CompareStrings(actual ?? "", expected, op);
    }

    private static bool CheckCommand(IInstallerConditionFacts facts, string command, string op, string expectedOutput)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var result = facts.RunCommand(command);
        if (string.IsNullOrWhiteSpace(expectedOutput))
            return CompareNumbers(result.ExitCode, 0, string.IsNullOrWhiteSpace(op) ? "==" : op);

        return op switch
        {
            "!=" => !result.Output.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase),
            "==" or "=" => result.Output.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase),
            _ => result.Output.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool CompareStrings(string actual, string expected, string op)
    {
        if (Version.TryParse(actual, out var actualVersion) && Version.TryParse(expected, out var expectedVersion))
            return Compare(actualVersion, expectedVersion, op);

        var comparison = string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "!=" => comparison != 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            "==" or "=" or "" => comparison == 0,
            _ => comparison == 0
        };
    }

    private static bool Compare(Version actual, Version expected, string op)
    {
        var comparison = actual.CompareTo(expected);
        return op switch
        {
            "!=" => comparison != 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            "==" or "=" or "" => comparison == 0,
            _ => comparison == 0
        };
    }

    private static bool CompareNumbers(int actual, int expected, string op)
        => op switch
        {
            "!=" => actual != expected,
            ">" => actual > expected,
            ">=" => actual >= expected,
            "<" => actual < expected,
            "<=" => actual <= expected,
            "==" or "=" or "" => actual == expected,
            _ => actual == expected
        };

    private static Version ParseVersionOrDefault(string value, Version fallback)
        => Version.TryParse(value, out var version) ? version : fallback;

    private static ConditionExpressionMode ParseExpression(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "any" => ConditionExpressionMode.Any,
            "not" => ConditionExpressionMode.Not,
            _ => ConditionExpressionMode.All
        };

    private static string Expand(string value, InstallerConditionEvaluationContext context)
    {
        var expanded = (value ?? "")
            .Replace("{InstallPath}", context.InstallRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            expanded = expanded
                .Replace("{" + variable.Key + "}", variable.Value, StringComparison.OrdinalIgnoreCase)
                .Replace("%" + variable.Key + "%", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    private static int IntInput(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value)
           && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";
}
