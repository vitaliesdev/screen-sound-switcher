using MonitorTv.Core;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MonitorTv.App;

/// <summary>
/// Checks the latest GitHub release and replaces the running executable with its ScreenSoundSwitcher.exe asset.
/// The download is only accepted from github.com over HTTPS and must match the release's .sha256 file.
/// </summary>
internal static class Updater
{
#if PORTABLE_BUILD
    private const string AssetName = "ScreenSoundSwitcher-portable.exe";
#else
    private const string AssetName = "ScreenSoundSwitcher.exe";
#endif

    private static readonly HttpClient Http = CreateClient();

    public sealed record Release(Version Version, string Tag, string AssetUrl, string ShaUrl, string PageUrl)
    {
        public string VersionText => $"v{Version.Major}.{Version.Minor}.{Version.Build}";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenSoundSwitcher-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The newest release if it is newer than this build and carries the exe and its checksum; otherwise null.</summary>
    public static async Task<Release?> CheckAsync(CancellationToken ct = default)
    {
        if (AppInfo.GitHubRepo == "") return null;

        using var response = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.GitHubRepo}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no releases yet
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= AppInfo.Version) return null;

        string? asset = null, sha = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == AssetName) asset = url;
            else if (name == AssetName + ".sha256") sha = url;
        }
        if (asset == null || sha == null) return null;

        return new Release(version, tag, asset, sha, root.GetProperty("html_url").GetString() ?? AppInfo.RepoUrl);
    }

    /// <summary>Downloads and verifies the new exe, then starts a helper that swaps it in after this process exits.</summary>
    public static async Task DownloadAndInstallAsync(Release release, IProgress<string> status, CancellationToken ct = default)
    {
        RequireGitHub(release.AssetUrl);
        RequireGitHub(release.ShaUrl);

        status.Report(Loc.T("Descargando la suma de comprobación..."));
        var shaText = await Http.GetStringAsync(release.ShaUrl, ct);
        var expected = shaText.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (expected.Length != 64) throw new InvalidDataException(Loc.T("La suma de comprobación de la versión no es válida."));

        var folder = Path.Combine(Path.GetTempPath(), "ScreenSoundSwitcher-update");
        Directory.CreateDirectory(folder);
        var newExe = Path.Combine(folder, AssetName);

        status.Report(Loc.T("Descargando {0}...", release.VersionText));
        using (var download = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            download.EnsureSuccessStatusCode();
            await using var source = await download.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(newExe);
            await source.CopyToAsync(output, ct);
        }

        status.Report(Loc.T("Verificando la descarga..."));
        string actual;
        await using (var file = File.OpenRead(newExe))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(newExe);
            throw new InvalidDataException(Loc.T("La descarga no coincide con su suma de comprobación; no se instala."));
        }

        var script = Path.Combine(folder, "update.ps1");
        File.WriteAllText(script, """
            param($ProcId, $New, $Target)
            Wait-Process -Id $ProcId -Timeout 30 -ErrorAction SilentlyContinue
            for ($i = 0; $i -lt 20; $i++) {
                try { Copy-Item -LiteralPath $New -Destination $Target -Force -ErrorAction Stop; break } catch { Start-Sleep -Seconds 1 }
            }
            Start-Process -FilePath $Target
            Remove-Item -LiteralPath $New -Force -ErrorAction SilentlyContinue
            """);

        var target = Environment.ProcessPath ?? throw new InvalidOperationException(Loc.T("No se conoce la ruta de la aplicación."));
        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                             Environment.ProcessId.ToString(), newExe, target },
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    private static void RequireGitHub(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
            throw new InvalidOperationException(Loc.T("Dirección de descarga no permitida: {0}", url));
    }
}
