using System.Reflection;
using Beep.Installer.Engine;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>Result of a field read or edit. <see cref="Error"/> is set when nothing was changed.</summary>
public sealed record ProjectFieldResult(bool Ok, string? Error, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Changed, string? BackupPath);

/// <summary>
/// The one owner of "read or change the top-level scalar fields of a .bsetup project" for headless callers
/// (the MCP server). It goes through <see cref="InstallerScriptSerializer"/> and <see cref="ProjectSchemaService"/>
/// like the builder does, so an edit that would make the project invalid is refused and the file is left untouched.
/// Collections (files, components, shortcuts, registry) are not scalar fields; they are changed in the builder.
/// </summary>
public static class ProjectFieldEditor
{
    private static readonly string[] SecretWords = { "password", "secret", "token", "pin", "credential", "key" };

    public static ProjectFieldResult List(string path)
    {
        var (project, error) = InstallerScriptSerializer.Load(path);
        if (project is null) return Fail(error ?? "Could not load the project.");
        var values = Fields().ToDictionary(p => p.Name, p => Display(p.GetValue(project)), StringComparer.OrdinalIgnoreCase);
        return new ProjectFieldResult(true, null, values, Array.Empty<string>(), null);
    }

    public static ProjectFieldResult Set(string path, IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Count == 0) return Fail("No fields were given.");
        var (project, error) = InstallerScriptSerializer.Load(path);
        if (project is null) return Fail(error ?? "Could not load the project.");

        var fields = Fields().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();
        foreach (var (name, raw) in changes)
        {
            if (!fields.TryGetValue(name, out var prop))
                return Fail($"'{name}' is not an editable scalar field. Use installer_get_project_fields to list them.");
            if (SecretWords.Any(w => prop.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                return Fail($"'{prop.Name}' may hold a credential; it is not editable here.");
            if (!TryConvert(raw, prop.PropertyType, out var value))
                return Fail($"'{raw}' is not a valid {Nullable.GetUnderlyingType(prop.PropertyType)?.Name ?? prop.PropertyType.Name} for '{prop.Name}'.");
            prop.SetValue(project, value);
            applied.Add(prop.Name);
        }

        var errors = ProjectSchemaService.Validate(project).Diagnostics
            .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error).Take(5).ToList();
        if (errors.Count > 0)
            return Fail("The edit would make the project invalid, so nothing was written: " + string.Join("; ", errors.Select(d => $"{d.Code} {d.Message}")));

        var backup = path + ".bak";
        File.Copy(path, backup, overwrite: true);
        var (saved, saveError) = InstallerScriptSerializer.Save(project, path);
        if (!saved) return Fail(saveError ?? "Saving failed.");
        var values = applied.ToDictionary(n => n, n => Display(fields[n].GetValue(project)), StringComparer.OrdinalIgnoreCase);
        return new ProjectFieldResult(true, null, values, applied, backup);
    }

    private static IEnumerable<PropertyInfo> Fields() =>
        typeof(InstallProject).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal);

    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(long) || t.IsEnum;
    }

    private static string? Display(object? v) => v?.ToString();

    private static bool TryConvert(string raw, Type type, out object? value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        value = null;
        if (target == typeof(string)) { value = raw; return true; }
        if (raw.Length == 0 && Nullable.GetUnderlyingType(type) is not null) return true;
        if (target == typeof(bool) && bool.TryParse(raw, out var b)) { value = b; return true; }
        if (target == typeof(int) && int.TryParse(raw, out var i)) { value = i; return true; }
        if (target == typeof(long) && long.TryParse(raw, out var l)) { value = l; return true; }
        if (target.IsEnum && Enum.TryParse(target, raw, ignoreCase: true, out var e) && Enum.IsDefined(target, e!)) { value = e; return true; }
        return false;
    }

    private static ProjectFieldResult Fail(string error) =>
        new(false, error, new Dictionary<string, string?>(), Array.Empty<string>(), null);
}
