using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace MonitorTv.Core.Profiles;

/// <summary>
/// The free-form "extra actions" of a profile, one per line:
/// <c>GET https://host/path</c> or <c>POST https://host/path {"json":true}</c> (a silent web call, e.g. Home Assistant or a smart plug),
/// a link such as <c>steam://open/bigpicture</c>, or a program with its arguments.
/// </summary>
public static class ExtraActions
{
    public abstract record Action;
    public sealed record WebCall(HttpMethod Method, Uri Url, string? Body) : Action;
    public sealed record Link(string Target) : Action;
    public sealed record Program(string File, string Arguments) : Action;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Action? Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;

        foreach (var method in new[] { "GET", "POST", "PUT" })
        {
            if (!line.StartsWith(method + " ", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = line[(method.Length + 1)..].Trim();
            var split = rest.IndexOf(' ');
            var url = split < 0 ? rest : rest[..split];
            var body = split < 0 ? null : rest[(split + 1)..].Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return new WebCall(new HttpMethod(method), uri, string.IsNullOrEmpty(body) ? null : body);
            throw new FormatException(Loc.T("Dirección no válida en «{0}».", line));
        }

        // a link: scheme://... without spaces (steam://, https://, ms-settings:...)
        if (!line.Contains(' ') && Uri.TryCreate(line, UriKind.Absolute, out var link) && link.Scheme != "file" && link.Scheme.Length > 1)
            return new Link(line);

        // a program: "C:\path with spaces\app.exe" args   |   app.exe args
        if (line.StartsWith('"'))
        {
            var end = line.IndexOf('"', 1);
            if (end < 0) throw new FormatException(Loc.T("Faltan unas comillas en «{0}».", line));
            return new Program(line[1..end], line[(end + 1)..].Trim());
        }
        var space = line.IndexOf(' ');
        return space < 0 ? new Program(line, "") : new Program(line[..space], line[(space + 1)..].Trim());
    }

    public static async Task RunAsync(string line, Action<string> log, CancellationToken ct = default)
    {
        var action = Parse(line);
        switch (action)
        {
            case null:
                return;

            case WebCall call:
                log($"{call.Method} {call.Url}");
                using (var request = new HttpRequestMessage(call.Method, call.Url))
                {
                    if (call.Body != null) request.Content = new StringContent(call.Body, Encoding.UTF8, "application/json");
                    using var response = await Http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                        log(Loc.T("La llamada devolvió {0} {1}.", (int)response.StatusCode, response.ReasonPhrase));
                }
                break;

            case Link link:
                log(Loc.T("Abriendo {0}", link.Target));
                Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true });
                break;

            case Program program:
                log(Loc.T("Ejecutando {0}", program.File));
                Process.Start(new ProcessStartInfo(program.File, program.Arguments) { UseShellExecute = true });
                break;
        }
    }
}
