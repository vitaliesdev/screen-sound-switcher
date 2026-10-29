using System.Reflection;

namespace MonitorTv.App;

internal static class AppInfo
{
    public const string Name = "Screen Sound Switcher";

    /// <summary>Ko-fi page, e.g. "https://ko-fi.com/yourname". Empty hides the donate button.</summary>
    public const string KofiUrl = "https://ko-fi.com/vitalies";

    /// <summary>GitHub repository as "owner/repo". Empty hides the code link and turns off update checks.</summary>
    public const string GitHubRepo = "vitaliesdev/screen-sound-switcher";

    public static string RepoUrl => GitHubRepo == "" ? "" : $"https://github.com/{GitHubRepo}";

    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    public static string VersionText => $"v{Version.Major}.{Version.Minor}.{Version.Build}";
}
