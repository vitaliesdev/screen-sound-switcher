using System.Text.Json;

namespace MonitorTv.Core;

/// <summary>When a program (or window title) appears or disappears, apply a profile.</summary>
public sealed class ProgramRule
{
    public bool Enabled { get; set; } = true;

    /// <summary>"process" (executable name, e.g. steam.exe), "title" (part of a window title) or "arctis" (the Arctis headset switching on or off).</summary>
    public string Match { get; set; } = "process";

    public string Text { get; set; } = "";

    /// <summary>Profile to apply when it opens (or the headset switches on); null for none.</summary>
    public string? OnStart { get; set; }

    /// <summary>Profile to apply when it closes (or the headset switches off); "@previous" goes back to the profile active before it opened.</summary>
    public string? OnStop { get; set; }
}

public sealed class AudioCycleSettings
{
    /// <summary>Output names in cycling order.</summary>
    public List<string> Outputs { get; set; } = [];

    public string? Hotkey { get; set; }
}

/// <summary>App-wide preferences, stored in settings.json next to the profiles.</summary>
public sealed class AppSettings
{
    /// <summary>Shows the advanced options (TV, programs, rules, controller...) in the profile editor.</summary>
    public bool AdvancedMode { get; set; }

    /// <summary>Restores the last confirmed monitor layout from anywhere, even with a black screen.</summary>
    public string EmergencyHotkey { get; set; } = "Ctrl+Alt+Shift+R";

    /// <summary>Seconds to confirm a new monitor layout before the previous one comes back.</summary>
    public int ConfirmSeconds { get; set; } = 15;

    /// <summary>
    /// When a changed layout must be confirmed: "always", "risky" (only when none of the screens that were on stays on, so the
    /// person could be left without a picture) or "never".
    /// </summary>
    public string ConfirmLayout { get; set; } = "risky";

    /// <summary>Puts the chosen layout back when Windows rearranges the screens after one disappears (see <c>DisplayGuard</c>).</summary>
    public bool RestoreScreens { get; set; } = true;

    public bool SafetyNoticeSeen { get; set; }

    /// <summary>"system" follows Windows' display language, otherwise a code from <see cref="Loc.Languages"/>.</summary>
    public string Language { get; set; } = "system";

    /// <summary>"system" follows Windows' light or dark mode, "light" and "dark" fix it.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>"system" follows Windows' animation setting, "on" always animates, "off" never does.</summary>
    public string Animations { get; set; } = "system";

    /// <summary>One shortcut that steps through a chosen set of sound outputs.</summary>
    public AudioCycleSettings AudioCycle { get; set; } = new();

    /// <summary>Profiles applied automatically when programs open or close.</summary>
    public List<ProgramRule> Rules { get; set; } = [];

    private static string FilePath => Path.Combine(AppData.Dir, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppData.Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
