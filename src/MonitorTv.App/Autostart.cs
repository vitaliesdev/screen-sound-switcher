using MonitorTv.Core;
using Microsoft.Win32;

namespace MonitorTv.App;

/// <summary>
/// "Start with Windows" for the current user, through the standard HKCU Run key (no admin rights, nothing outside the
/// user's profile). It is only ever written when the user turns the option on, and removed when they turn it off.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenSoundSwitcher";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
    }

    /// <summary>Starts the app hidden in the tray at sign-in.</summary>
    public static void Enable()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(Loc.T("No se conoce la ruta de la aplicación."));
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exe}\" --tray");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
