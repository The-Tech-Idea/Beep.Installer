using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

/// <summary>
/// An operation being authored: a provider type plus the values collected for it.
///
/// Deliberately free of any control. The rules that decide whether a draft is usable — required
/// inputs, integers that parse, choices that are actually offered — are the interesting part, and
/// they have nothing to do with widgets.
/// </summary>
public sealed class ResourceDraft
{
    public required string Type { get; init; }

    /// <summary>Scalar input values, keyed by <see cref="ResourceInput.Key"/>.</summary>
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Rows of each repeatable group, keyed by <see cref="ResourceInputList.Prefix"/>.</summary>
    public Dictionary<string, List<Dictionary<string, string>>> Lists { get; init; } =
        new(StringComparer.Ordinal);

    public string Value(string key) => Values.TryGetValue(key, out var v) ? v ?? "" : "";

    public List<Dictionary<string, string>> Rows(string prefix)
    {
        if (!Lists.TryGetValue(prefix, out var rows))
        {
            rows = new List<Dictionary<string, string>>();
            Lists[prefix] = rows;
        }
        return rows;
    }
}

/// <summary>One reason a draft cannot be turned into an operation.</summary>
/// <param name="Key">The input at fault, so a UI can mark the field rather than only the dialog.</param>
public sealed record ResourceDraftProblem(string Key, string Message);

/// <summary>
/// Creates, checks and compiles <see cref="ResourceDraft"/>s.
///
/// This is what makes typed resources authorable: the builder previously offered the raw
/// <c>Inputs</c> dictionary, so the only way to compose a valid operation was to already know every
/// key the provider reads. Everything here is driven by <see cref="ResourceInputCatalog"/>, which is
/// held to the providers by <c>ResourceCatalogCoverageTests</c>.
/// </summary>
public static class ResourceDraftBuilder
{
    /// <summary>A draft of <paramref name="type"/> pre-filled with the catalog's defaults.</summary>
    public static ResourceDraft CreateDefault(string type)
    {
        if (!ResourceInputCatalog.TryGet(type, out var descriptor))
            throw new ArgumentException($"No resource provider is described for '{type}'.", nameof(type));

        var draft = new ResourceDraft { Type = descriptor.Type };

        foreach (var input in descriptor.Inputs)
            draft.Values[input.Key] = input.Default;

        foreach (var group in descriptor.Repeatables)
            draft.Lists[group.Prefix] = new List<Dictionary<string, string>>();

        return draft;
    }

    /// <summary>A row for a repeatable group, pre-filled with that group's defaults.</summary>
    public static Dictionary<string, string> CreateRow(ResourceInputList group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in group.Fields)
            row[field.Key] = field.Default;
        return row;
    }

    /// <summary>Everything wrong with the draft, in the order the fields are shown.</summary>
    public static IReadOnlyList<ResourceDraftProblem> Validate(ResourceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var problems = new List<ResourceDraftProblem>();

        if (!ResourceInputCatalog.TryGet(draft.Type, out var descriptor))
        {
            problems.Add(new ResourceDraftProblem("", $"No resource provider is described for '{draft.Type}'."));
            return problems;
        }

        foreach (var input in descriptor.Inputs)
            Check(problems, input, draft.Value(input.Key), input.Key);

        foreach (var group in descriptor.Repeatables)
        {
            var rows = draft.Lists.TryGetValue(group.Prefix, out var r) ? r : new List<Dictionary<string, string>>();

            for (var i = 0; i < rows.Count; i++)
            {
                foreach (var field in group.Fields)
                {
                    var value = rows[i].TryGetValue(field.Key, out var v) ? v ?? "" : "";
                    Check(problems, field, value, $"{group.Prefix}.{i}.{field.Key}");
                }
            }
        }

        return problems;
    }

    private static void Check(List<ResourceDraftProblem> problems, ResourceInput input, string value, string key)
    {
        if (input.Required && string.IsNullOrWhiteSpace(value))
        {
            problems.Add(new ResourceDraftProblem(key, $"{input.Label} is required."));
            return;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        switch (input.Kind)
        {
            case ResourceInputKind.Integer when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _):
                problems.Add(new ResourceDraftProblem(key, $"{input.Label} must be a whole number."));
                break;

            case ResourceInputKind.Bool when !bool.TryParse(value, out _):
                problems.Add(new ResourceDraftProblem(key, $"{input.Label} must be true or false."));
                break;

            case ResourceInputKind.Choice when input.Options.Count > 0
                                               && !input.Options.Contains(value, StringComparer.OrdinalIgnoreCase):
                problems.Add(new ResourceDraftProblem(key,
                    $"{input.Label} must be one of: {string.Join(", ", input.Options.Where(o => o.Length > 0))}."));
                break;
        }
    }

    /// <summary>
    /// Compiles the draft into an operation the plan executor can run.
    /// </summary>
    /// <remarks>
    /// Empty values are dropped rather than written as blanks: a provider distinguishes "not
    /// supplied" from "supplied as empty", and several treat the latter as an explicit override.
    ///
    /// Inputs declared <see cref="ResourceInputKind.Secret"/> are recorded in
    /// <c>SensitiveInputs</c>, which is what keeps a signing password or a service account password
    /// out of logs and evidence files. Nothing populated that list before, so every secret authored
    /// through the old dictionary grid was logged in clear.
    /// </remarks>
    public static CompiledInstallOperation Build(ResourceDraft draft, string id, string displayName = "")
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An operation id is required.", nameof(id));

        if (!ResourceInputCatalog.TryGet(draft.Type, out var descriptor))
            throw new ArgumentException($"No resource provider is described for '{draft.Type}'.", nameof(draft));

        var operation = new CompiledInstallOperation
        {
            Id = id,
            Type = descriptor.Type,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? descriptor.Label : displayName,
            RollbackSupported = true,
        };

        foreach (var input in descriptor.Inputs)
        {
            var value = draft.Value(input.Key);
            if (string.IsNullOrWhiteSpace(value)) continue;

            operation.Inputs[input.Key] = value;
            if (input.Kind == ResourceInputKind.Secret)
                operation.SensitiveInputs.Add(input.Key);
        }

        foreach (var group in descriptor.Repeatables)
        {
            var rows = draft.Lists.TryGetValue(group.Prefix, out var r) ? r : null;
            if (rows == null || rows.Count == 0) continue;

            operation.Inputs[group.CountKey] = rows.Count.ToString(CultureInfo.InvariantCulture);

            for (var i = 0; i < rows.Count; i++)
            {
                foreach (var field in group.Fields)
                {
                    var value = rows[i].TryGetValue(field.Key, out var v) ? v ?? "" : "";
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    var key = $"{group.Prefix}.{i}.{field.Key}";
                    operation.Inputs[key] = value;
                    if (field.Kind == ResourceInputKind.Secret)
                        operation.SensitiveInputs.Add(key);
                }
            }
        }

        return operation;
    }

    /// <summary>
    /// Reads an existing operation back into a draft, so the wizard can edit one rather than only
    /// create it.
    /// </summary>
    public static ResourceDraft FromOperation(CompiledInstallOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var draft = CreateDefault(operation.Type);

        if (!ResourceInputCatalog.TryGet(operation.Type, out var descriptor)) return draft;

        foreach (var input in descriptor.Inputs)
        {
            if (operation.Inputs.TryGetValue(input.Key, out var value))
                draft.Values[input.Key] = value ?? "";
        }

        foreach (var group in descriptor.Repeatables)
        {
            var count = operation.Inputs.TryGetValue(group.CountKey, out var raw)
                        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            var rows = draft.Rows(group.Prefix);
            rows.Clear();

            for (var i = 0; i < count; i++)
            {
                var row = CreateRow(group);
                foreach (var field in group.Fields)
                {
                    if (operation.Inputs.TryGetValue($"{group.Prefix}.{i}.{field.Key}", out var value))
                        row[field.Key] = value ?? "";
                }
                rows.Add(row);
            }
        }

        return draft;
    }

    /// <summary>An id that does not collide with the operations already in the project.</summary>
    public static string NextId(string type, IEnumerable<CompiledInstallOperation> existing)
    {
        var taken = existing?.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var n = 1; ; n++)
        {
            var candidate = $"{type}:{n}";
            if (taken.Add(candidate)) return candidate;
        }
    }
}
