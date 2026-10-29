namespace MonitorTv.Core;

/// <summary>
/// Where profiles and TV settings live: %AppData%\ScreenSoundSwitcher.
/// SSS_DATA overrides it (used for tests and snapshots).
/// </summary>
public static class AppData
{
    public static string Dir { get; } =
        Environment.GetEnvironmentVariable("SSS_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenSoundSwitcher");
}
