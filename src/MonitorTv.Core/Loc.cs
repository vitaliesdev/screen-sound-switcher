using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MonitorTv.Core;

/// <summary>
/// Interface texts. The source text is Spanish and doubles as the key: <c>Loc.T("Guardar cambios")</c> returns the text in the
/// current language, or the Spanish itself when there is no translation. Translations live in Strings/&lt;code&gt;.json
/// (a flat map from the Spanish text to the translated text) and are embedded in this assembly.
/// </summary>
public static class Loc
{
    /// <summary>The supported languages: code and the language's own name (shown in the language menu).</summary>
    public static readonly (string Code, string Native)[] Languages =
    [
        ("es", "Español"), ("en", "English"), ("fr", "Français"), ("de", "Deutsch"), ("pt-BR", "Português (Brasil)"),
        ("it", "Italiano"), ("ru", "Русский"), ("uk", "Українська"), ("zh-Hans", "简体中文"), ("ja", "日本語"),
    ];

    private static Dictionary<string, string> _map = [];

    /// <summary>The language in use, one of <see cref="Languages"/>.</summary>
    public static string Current { get; private set; } = "es";

    /// <summary>Selects the language: "system" follows Windows' display language, otherwise a code from <see cref="Languages"/>.</summary>
    public static void Use(string setting)
    {
        var code = setting == "system" ? FromWindows() : setting;
        if (!Languages.Any(l => l.Code == code)) code = "en";
        Current = code;
        _map = Load(code);
    }

    /// <summary>Windows' display language mapped to a supported one; English for anything else.</summary>
    public static string FromWindows()
    {
        var culture = CultureInfo.CurrentUICulture;
        var two = culture.TwoLetterISOLanguageName;
        return two switch
        {
            "pt" => "pt-BR",
            "zh" => "zh-Hans",
            _ => Languages.Any(l => l.Code == two) ? two : "en",
        };
    }

    public static string T(string source) => _map.TryGetValue(source, out var translated) && translated.Length > 0 ? translated : source;

    public static string T(string source, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(source), args);

    private static Dictionary<string, string> Load(string code)
    {
        // Spanish is the source text, so its file only holds wording refinements (old text -> clearer text); the other
        // languages keep the original text as their key.
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Strings.{code}.json");
        if (stream == null) return [];
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
