using System.Diagnostics;
using System.Windows.Threading;
using MonitorTv.Core;
using MonitorTv.Core.Audio;
using MonitorTv.Core.Devices;

namespace MonitorTv.App;

/// <summary>
/// Reacts to a program (or window title) appearing or disappearing, or to the Arctis headset switching on or off, by applying a profile
/// and/or changing the sound output, and can put things back when it ends.
/// </summary>
public sealed class RulesEngine : IDisposable
{
    /// <summary>When it ends: go back to the profile and the sound output that were active when it started.</summary>
    public const string Previous = "@previous";

    /// <summary>An action that switches the default sound output: "audio:&lt;output name&gt;".</summary>
    public const string AudioPrefix = "audio:";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Func<IReadOnlyList<ProgramRule>> _rules;
    private readonly Func<string?> _activeProfile;
    private readonly Action<string> _applyProfile;
    private readonly Action<string, NoticeKind> _notify;
    private readonly Dictionary<ProgramRule, bool> _running = [];
    private readonly Dictionary<ProgramRule, string?> _before = [];
    private readonly Dictionary<ProgramRule, string?> _beforeAudio = [];
    private bool _first = true;

    // The headset is asked on a background thread (a HID request can take a moment); the timer only reads the latest answer.
    private ArctisHeadset.Monitor? _arctis;
    private CancellationTokenSource? _arctisStop;
    private volatile int _arctisOn = -1; // -1 unknown, 0 off, 1 on

    public RulesEngine(Func<IReadOnlyList<ProgramRule>> rules, Func<string?> activeProfile, Action<string> applyProfile, Action<string, NoticeKind> notify)
    {
        _rules = rules;
        _activeProfile = activeProfile;
        _applyProfile = applyProfile;
        _notify = notify;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        var rules = _rules().Where(r => r.Enabled && (r.Match == "arctis" || r.Text.Trim() != "")).ToList();
        SyncArctisPolling(rules.Any(r => r.Match == "arctis"));
        if (rules.Count == 0) { _first = true; _running.Clear(); return; }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titles = new List<string>();
        var needTitles = rules.Any(r => r.Match == "title");
        if (rules.Any(r => r.Match is "process" or "title"))
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    names.Add(p.ProcessName);
                    if (needTitles && p.MainWindowTitle.Length > 0) titles.Add(p.MainWindowTitle);
                }
                catch { /* process ended or is not accessible */ }
                finally { p.Dispose(); }
            }
        }

        foreach (var rule in rules)
        {
            bool running;
            if (rule.Match == "arctis")
            {
                if (_arctisOn < 0) continue; // no answer from the base yet
                running = _arctisOn == 1;
            }
            else
            {
                var text = rule.Text.Trim();
                running = rule.Match == "title"
                    ? titles.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase))
                    : names.Contains(text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text[..^4] : text);
            }

            var wasKnown = _running.TryGetValue(rule, out var was);
            _running[rule] = running;
            if (_first || !wasKnown && rule.Match == "arctis") continue; // the first pass only learns what is already there
            if (running == was) continue;

            if (running)
            {
                // remember how things were, so "go back" has something to go back to
                _before[rule] = _activeProfile();
                _beforeAudio[rule] = SafeCurrentOutputId();
                Run(rule.OnStart);
            }
            else
            {
                Run(rule.OnStop, rule);
            }
        }
        _first = false;
    }

    /// <summary>Runs one action: a profile, a sound output, or going back (only meaningful when a rule ends).</summary>
    private void Run(string? action, ProgramRule? ending = null)
    {
        if (action == null) return;

        if (action == Previous)
        {
            if (ending == null) return;
            if (_before.GetValueOrDefault(ending) is { } profile) _applyProfile(profile);
            if (_beforeAudio.GetValueOrDefault(ending) is { } outputId) SetOutputById(outputId);
        }
        else if (action.StartsWith(AudioPrefix))
        {
            SetOutputByName(action[AudioPrefix.Length..]);
        }
        else
        {
            _applyProfile(action);
        }
    }

    private static string? SafeCurrentOutputId()
    {
        try { return AudioOutputs.Current()?.Id; }
        catch { return null; }
    }

    private void SetOutputByName(string name)
    {
        try
        {
            var output = AudioOutputs.Find(name);
            if (output == null) { _notify(Loc.T("No se encontró la salida de sonido «{0}».", name), NoticeKind.Warning); return; }
            AudioOutputs.SetDefault(output.Id);
            _notify(Loc.T("Sonido: {0}", output.Name), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _notify(Loc.T("No se pudo cambiar el sonido: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private void SetOutputById(string id)
    {
        try
        {
            var output = AudioOutputs.List().FirstOrDefault(o => o.Id == id);
            if (output == null) return; // the old output is gone; leave things as they are
            AudioOutputs.SetDefault(output.Id);
            _notify(Loc.T("Sonido: {0}", output.Name), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _notify(Loc.T("No se pudo cambiar el sonido: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private void SyncArctisPolling(bool needed)
    {
        if (needed && _arctisStop == null)
        {
            _arctis = new ArctisHeadset.Monitor();
            var stop = _arctisStop = new CancellationTokenSource();
            var monitor = _arctis;
            _ = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var reading = monitor.Read();
                    if (reading.State is ArctisHeadset.State.On or ArctisHeadset.State.Charging) _arctisOn = 1;
                    else if (reading.State == ArctisHeadset.State.Off) _arctisOn = 0;
                    // NoBase (unplugged or busy): keep the last known state instead of reporting a false switch-off
                    try { await Task.Delay(1500, stop.Token); } catch (OperationCanceledException) { }
                }
            });
        }
        else if (!needed && _arctisStop != null)
        {
            _arctisStop.Cancel();
            _arctisStop = null;
            _arctis?.Dispose();
            _arctis = null;
            _arctisOn = -1;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        SyncArctisPolling(false);
    }
}
