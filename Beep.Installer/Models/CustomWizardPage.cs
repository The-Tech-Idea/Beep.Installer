using System.Collections.Generic;

namespace Beep.Installer.Models;

/// <summary>
/// A user-defined wizard page beyond the built-in installer flow.
/// Rendered by <c>Pages/CustomPage</c>; answers land in <c>InstallContext.Bag["Custom:&lt;fieldId&gt;"]</c>
/// and are exposed to custom actions / config via <c>{Custom:&lt;fieldId&gt;}</c> macros.
/// </summary>
public class CustomWizardPage
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    /// <summary>Insert position hint among the built-in pages (higher = later). Custom pages are
    /// currently grouped just before the final review/complete page, ordered by this value.</summary>
    public int Order { get; set; }
    public List<CustomField> Fields { get; set; } = new();
}

/// <summary>A single input on a <see cref="CustomWizardPage"/>.</summary>
public class CustomField
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public CustomFieldType Type { get; set; } = CustomFieldType.Text;
    public string DefaultValue { get; set; } = "";
    /// <summary>For <see cref="CustomFieldType.Radio"/>: the choices.</summary>
    public List<string> Options { get; set; } = new();
    public bool Required { get; set; }
    /// <summary>Optional macro name this value binds to at runtime (e.g. "license").</summary>
    public string DestinationMacro { get; set; } = "";
}

public enum CustomFieldType { Text, Check, Radio, Path, Password }
