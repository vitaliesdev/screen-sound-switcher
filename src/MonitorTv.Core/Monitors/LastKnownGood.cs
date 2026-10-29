using System.Text.Json;

namespace MonitorTv.Core.Monitors;

/// <summary>The last monitor layout the user confirmed was working, kept on disk so it can be restored in an emergency.</summary>
public static class LastKnownGood
{
    private static string FilePath => Path.Combine(AppData.Dir, "last-good.json");

    public static void Save(DisplayConfig.Layout layout)
    {
        Directory.CreateDirectory(AppData.Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
    }

    public static DisplayConfig.Layout? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<DisplayConfig.Layout>(File.ReadAllText(FilePath)) : null; }
        catch (JsonException) { return null; }
    }

    public static bool Same(DisplayConfig.Layout a, DisplayConfig.Layout b) => a.Paths == b.Paths && a.Modes == b.Modes;

    /// <summary>Puts the last confirmed layout back, re-pointed at today's adapter numbering.</summary>
    public static bool Restore()
    {
        if (Load() is not { } layout) return false;
        DisplayConfig.Apply(DisplayIdentity.Refresh(layout));
        return true;
    }
}
