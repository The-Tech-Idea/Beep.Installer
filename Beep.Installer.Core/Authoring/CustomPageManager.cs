using System;
using System.Collections.Generic;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>
/// Field-level behaviour for user-defined wizard pages: required-field validation, answer
/// collection (applying defaults), and <c>{Custom:fieldId}</c> macro expansion.
///
/// This is the single implementation. The page-level helpers on <see cref="InstallProject"/>
/// delegate here so the model stays a data contract rather than carrying behaviour — the
/// direction the authoring core takes in a later phase.
/// </summary>
public static class CustomPageManager
{
    /// <summary>
    /// Verifies every <see cref="CustomField.Required"/> field has a usable answer.
    /// An unchecked checkbox ("false") counts as unanswered, matching the wizard's behaviour
    /// of using required checkboxes as consent gates.
    /// </summary>
    public static (bool ok, string? error) Validate(
        IEnumerable<CustomField> fields,
        IDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var field in fields)
        {
            if (!field.Required) continue;
            values.TryGetValue(field.Id, out var value);
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"'{field.Label}' is required.");
            }
        }
        return (true, null);
    }

    /// <summary>
    /// Returns an answer for every field, falling back to
    /// <see cref="CustomField.DefaultValue"/> when the user supplied nothing.
    /// </summary>
    public static Dictionary<string, string> Collect(
        IEnumerable<CustomField> fields,
        IDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);

        var result = new Dictionary<string, string>();
        foreach (var field in fields)
        {
            result[field.Id] = values.TryGetValue(field.Id, out var value)
                ? value ?? ""
                : field.DefaultValue ?? "";
        }
        return result;
    }

    /// <summary>
    /// Substitutes <c>{Custom:fieldId}</c> tokens. Unknown tokens are deliberately left
    /// intact rather than blanked, so a typo is visible in the output instead of silently
    /// producing an empty value.
    /// </summary>
    public static string ExpandMacros(string text, IDictionary<string, string>? values)
    {
        if (string.IsNullOrEmpty(text) || values == null || values.Count == 0)
            return text ?? "";

        foreach (var pair in values)
            text = text.Replace("{Custom:" + pair.Key + "}", pair.Value ?? "");

        return text;
    }
}
