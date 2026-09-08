namespace Beep.Installer.Lang;

/// <summary>
/// The one way a form asks for a translated string.
///
/// The Package Builder had a private <c>L</c> helper and the twelve dialogs had nothing, so
/// everything outside the wizard pages was hard-coded English — the language switcher changed the
/// installer the user sees and left the tool that authors it untouched. A shared helper means a new
/// form does not have to reinvent (or forget) the lookup.
///
/// The English text is passed alongside the key and used when the key is missing, so routing a
/// string through here is behaviour-preserving: an untranslated key reads exactly as it did before.
/// </summary>
public static class UiStrings
{
    /// <param name="key">Resource key, <c>Area_Meaning</c> — stable once shipped; translators key off it.</param>
    /// <param name="english">The English text, and the fallback when the key is not defined.</param>
    public static string L(string key, string english) => LanguageManager.GetOrDefault(key, english);
}
