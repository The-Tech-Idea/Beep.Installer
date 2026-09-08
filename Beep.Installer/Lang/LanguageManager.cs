using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Xml.Linq;
using Beep.Installer.Engine;

namespace Beep.Installer.Lang;

/// <summary>
/// Multi-language support for the installer UI.
///
/// Resolution order:
///   1. Embedded Lang.Strings_xx (raw .resx XML loaded via ResXResourceReader).
///   2. Sibling Lang\Strings_xx.resx file on disk.
///   3. Base language (en).
///   4. The key itself.
/// </summary>
public static class LanguageManager
{
    private const string ResourcePrefix = "Beep.Installer.Lang.Strings_";
    private static readonly string _looseLangDir = Path.Combine(AppContext.BaseDirectory, "Lang");
    private static CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en");
    private static Dictionary<string, string>? _currentStrings;

    public static CultureInfo CurrentCulture => _currentCulture;

    /// <summary>Languages we ship translations for.</summary>
    public static readonly string[] SupportedCultures = { "en", "ar", "es", "fr", "de", "zh", "ja", "pt" };

    public static string CurrentLanguageName => _currentCulture.TwoLetterISOLanguageName switch
    {
        "ar" => "العربية",
        "es" => "Español",
        "fr" => "Français",
        "de" => "Deutsch",
        "zh" => "中文",
        "ja" => "日本語",
        "pt" => "Português",
        _ => "English"
    };

    /// <summary>Initialize from the system UI culture.</summary>
    public static void Initialize() => SetLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>
    /// Raised after the language actually changes, so UI already on screen can re-read its text.
    ///
    /// Without this, switching language only affected windows opened afterwards: every caption
    /// already rendered kept the old language, because <c>GetString</c> is pull-only and nothing
    /// told anyone to pull again.
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Switch to a specific two-letter language code (e.g. "en", "fr").</summary>
    public static void SetLanguage(string twoLetterCode)
    {
        if (string.IsNullOrWhiteSpace(twoLetterCode)) twoLetterCode = "en";
        twoLetterCode = twoLetterCode.Trim().ToLowerInvariant();
        if (twoLetterCode.Length > 2) twoLetterCode = twoLetterCode[..2];
        if (!SupportedCultures.Contains(twoLetterCode)) twoLetterCode = "en";

        // Only announce a real change: re-selecting the current language should not churn the UI.
        var changed = !string.Equals(_currentCulture.TwoLetterISOLanguageName, twoLetterCode, StringComparison.Ordinal);

        _currentCulture = CultureInfo.GetCultureInfo(twoLetterCode);
        _currentStrings = LoadStrings(twoLetterCode);

        if (changed) RaiseLanguageChanged();
    }

    /// <summary>
    /// Notifies listeners. A handler that throws — a disposed form, typically — must not stop the
    /// rest of the UI from re-reading its strings, or a switch leaves the window half-translated.
    /// </summary>
    private static void RaiseLanguageChanged()
    {
        foreach (var handler in (LanguageChanged?.GetInvocationList() ?? Array.Empty<Delegate>()).Cast<EventHandler>())
        {
            try { handler(null, EventArgs.Empty); }
            catch (Exception ex) { Engine.Diag.Debug("LanguageManager", "a language-changed listener threw", ex); }
        }
    }

    /// <summary>Get a localized string by key. Returns the key itself if not found.</summary>
    public static string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        _currentStrings ??= LoadStrings(_currentCulture.TwoLetterISOLanguageName);
        if (_currentStrings != null && _currentStrings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;

        // Fall back to English (only if we are not already English)
        if (_currentCulture.TwoLetterISOLanguageName != "en")
        {
            var en = LoadStrings("en");
            if (en.TryGetValue(key, out var enVal) && !string.IsNullOrEmpty(enVal)) return enVal;
        }
        return key;
    }

    /// <summary>Get a formatted localized string.</summary>
    public static string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        return args.Length > 0 ? string.Format(format, args) : format;
    }

    /// <summary>Try to get a localized string. Returns false if the key is not found in any resource.</summary>
    public static bool TryGetString(string key, out string value)
    {
        value = "";
        if (string.IsNullOrEmpty(key)) return false;
        if (_currentStrings != null && _currentStrings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        if (_currentCulture.TwoLetterISOLanguageName != "en")
        {
            var en = LoadStrings("en");
            if (en.TryGetValue(key, out var enVal) && !string.IsNullOrEmpty(enVal))
            {
                value = enVal;
                return true;
            }
        }
        return false;
    }

    /// <summary>Get a string with an English fallback if the key is missing in the current language.</summary>
    public static string GetOrDefault(string key, string defaultValue)
        => TryGetString(key, out var v) ? v : defaultValue;

    /// <summary>
    /// Every key defined for a culture, loaded through the same path the wizard uses.
    /// Returning empty means that culture's resources are not reachable at runtime.
    /// </summary>
    public static IReadOnlyCollection<string> GetKeys(string twoLetterCode)
        => LoadStrings(twoLetterCode).Keys.ToArray();

    public static string T(string key) => GetString(key);
    public static string Tf(string key, params object[] args) => GetString(key, args);

    // ── Internal loading ──

    private static Dictionary<string, string> LoadStrings(string twoLetterCode)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var asm = typeof(LanguageManager).Assembly;

        // 1) Embedded resources.
        //
        //    The SDK compiles .resx files into BINARY .resources at build time, so the
        //    embedded name is "Beep.Installer.Lang.Strings_xx.resources" — not ".resx".
        //    This list previously probed only .resx shapes and read them with
        //    ResXResourceReader, so nothing ever matched, every lookup fell through to the
        //    key/default, and all eight translations were dead at runtime. The .resources
        //    entry must therefore come first; the .resx shapes are kept for loose/dev builds.
        var candidates = new[]
        {
            ResourcePrefix + twoLetterCode + ".resources",
            ResourcePrefix + twoLetterCode + ".resx",
            ResourcePrefix + twoLetterCode,
            "Lang.Strings_" + twoLetterCode + ".resx",
            "Strings_" + twoLetterCode + ".resx"
        };
        foreach (var name in candidates)
        {
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;

                // Binary .resources needs ResourceReader; raw .resx needs ResXResourceReader.
                if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    using var binary = new ResourceReader(stream);
                    foreach (System.Collections.DictionaryEntry e in binary)
                        if (e.Key is string bk && e.Value is string bs) dict[bk] = bs;
                }
                else
                {
                    using var reader = new ResXResourceReader(stream);
                    foreach (System.Collections.DictionaryEntry e in reader)
                        if (e.Key is string k && e.Value is string s) dict[k] = s;
                }

                if (dict.Count > 0) break;
            }
            catch (Exception ex) { Diag.Debug("LanguageManager", $"resource '{name}' skipped", ex); }
        }

        // 2) Loose .resx file
        if (dict.Count == 0)
        {
            var path = Path.Combine(_looseLangDir, $"Strings_{twoLetterCode}.resx");
            if (File.Exists(path))
            {
                try
                {
                    var doc = XDocument.Load(path);
                    foreach (var data in doc.Descendants("data"))
                    {
                        var name = data.Attribute("name")?.Value;
                        var value = data.Element("value")?.Value;
                        if (!string.IsNullOrEmpty(name) && value != null) dict[name] = value;
                    }
                }
                catch (Exception ex) { Diag.Debug("LanguageManager", $"loose .resx parse failed for {path}", ex); }
            }
        }

        return dict;
    }
}
