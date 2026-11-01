using System.Text.Json;
using MonitorTv.Core.Audio;
using MonitorTv.Core.Monitors;
using MonitorTv.Core.Tv;
using System.Diagnostics;

namespace MonitorTv.Core.Profiles;

/// <summary>A combined profile: a monitor layout and an audio output, applied together.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "";

    /// <summary>Monitor layout captured with <see cref="ProfileStore.SaveCurrentMonitors"/>; null leaves monitors alone.</summary>
    public DisplayConfig.Layout? Monitors { get; set; }

    /// <summary>Part of the playback device name to make default; null leaves audio alone.</summary>
    public string? AudioOutput { get; set; }

    /// <summary>How long to wait for the audio output to appear, e.g. a TV that is still waking up.</summary>
    public int AudioTimeoutSeconds { get; set; } = 20;

    /// <summary>"on" wakes the TV before anything else, "off" turns it off at the end; null leaves it alone.</summary>
    public string? TvPower { get; set; }

    /// <summary>HDMI_1..HDMI_4 or TV; null leaves the TV input alone.</summary>
    public string? TvInput { get; set; }

    /// <summary>Global shortcut that applies the profile, e.g. "Ctrl+Alt+F1"; null for none.</summary>
    public string? Hotkey { get; set; }

    /// <summary>XInput button mask of a controller combination that applies this profile; null for none.</summary>
    public int? ControllerCombo { get; set; }

    /// <summary>Programs or URIs started last, e.g. steam://open/bigpicture or steam://close/bigpicture.</summary>
    public List<string> Launch { get; set; } = [];
}

public sealed class ProfileStore(string? root = null)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; } = root ?? Path.Combine(AppData.Dir, "Profiles");

    private string PathFor(string name) => Path.Combine(Root, name + ".json");

    public IEnumerable<string> List() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*.json").Select(Path.GetFileNameWithoutExtension).Order()!
            : [];

    public Profile Load(string name)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(PathFor(name)), Json)
                ?? throw new InvalidDataException(Loc.T("El perfil '{0}' está vacío.", name));
            profile.Name = name;          // the file name is the profile's name, whatever the file says
            profile.Launch ??= [];        // a hand-edited "Launch": null must not break the editor
            return profile;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Loc.T("El perfil '{0}' está dañado y no se puede leer.", name), ex);
        }
    }

    /// <summary>Every readable profile; the name of a damaged one is passed to <paramref name="onDamaged"/> and its file left untouched.</summary>
    public List<Profile> LoadAll(Action<string>? onDamaged = null)
    {
        var profiles = new List<Profile>();
        foreach (var name in List())
        {
            try { profiles.Add(Load(name)); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { onDamaged?.Invoke(name!); }
        }
        return profiles;
    }

    public void Save(Profile profile)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(PathFor(profile.Name), JsonSerializer.Serialize(profile, Json));
    }

    public void Delete(string name)
    {
        var file = PathFor(name);
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>
    /// Profiles saved before monitors were identified by device path get theirs now. Only sound if the stored adapter
    /// numbers are still current (same boot as the capture); otherwise Windows reports no match and the profile is left alone.
    /// </summary>
    public int MigrateTargets()
    {
        var migrated = 0;
        foreach (var profile in LoadAll())
        {
            if (profile.Monitors is not { Targets: null } layout) continue;
            var withTargets = DisplayIdentity.WithTargets(layout);
            if (withTargets.Targets == null) continue;
            profile.Monitors = withTargets;
            Save(profile);
            migrated++;
        }
        return migrated;
    }

    /// <summary>Updates (or creates) a profile with the monitor layout that is active right now.</summary>
    public Profile SaveCurrentMonitors(string name)
    {
        var profile = File.Exists(PathFor(name)) ? Load(name) : new Profile { Name = name };
        profile.Monitors = DisplayConfig.Capture();
        Save(profile);
        return profile;
    }
}

public static class ProfileRunner
{
    /// <summary>
    /// Whether the person must confirm the layout that was just applied. In "risky" mode that is only when none of the screens
    /// that were on stays on: with one still lit there is always a picture to read, and a change made with a shortcut while
    /// leaving (or on a TV nobody can click) is not undone by an unanswered countdown.
    /// </summary>
    public static bool NeedsConfirmation(string mode, DisplayConfig.Layout before, DisplayConfig.Layout after)
    {
        if (LastKnownGood.Same(before, after)) return false;
        return mode switch
        {
            "never" => false,
            "risky" => !(before.Targets is { Count: > 0 } was && after.Targets is { Count: > 0 } now &&
                         was.Any(a => now.Any(b => b.DevicePath.Equals(a.DevicePath, StringComparison.OrdinalIgnoreCase)))),
            _ => true,
        };
    }

    /// <summary>
    /// Applies a profile. When the monitor layout changes, <paramref name="confirmMonitors"/> is asked whether the new
    /// layout is usable; if it answers false (or is never answered) the previous layout is restored and the run stops.
    /// Returns false when the layout was reverted.
    /// </summary>
    public static async Task<bool> Run(Profile profile, Action<string> log, CancellationToken ct = default,
        Func<Task<bool>>? confirmMonitors = null, string confirmMode = "always")
    {
        var tvSettings = TvSettings.Load();

        if (profile.TvPower == "on")
        {
            log(Loc.T("Encendiendo la tele..."));
            LgTv.WakeOnLan(tvSettings.Mac);
        }

        if (profile.TvInput != null)
        {
            log(Loc.T("Cambiando la entrada de la tele a {0}...", profile.TvInput));
            using var tv = await LgTv.Connect(tvSettings, TimeSpan.FromSeconds(30), ct: ct);
            await tv.SetInput(profile.TvInput, ct);
        }

        if (profile.Monitors != null)
        {
            var missing = DisplayIdentity.MissingMonitors(profile.Monitors);
            if (missing.Count > 0)
                throw new InvalidOperationException(Loc.T("Falta un monitor de este perfil ({0} sin detectar).", missing.Count));

            var before = DisplayConfig.Capture();
            // already like this (a rule fired again, the shortcut pressed twice): no flicker and no pointless countdown
            if (LastKnownGood.Same(before, DisplayIdentity.Refresh(profile.Monitors)))
            {
                log(Loc.T("Las pantallas ya están como en este perfil."));
            }
            else
            {
                log(Loc.T("Aplicando la disposición de pantallas..."));
                DisplayConfig.Apply(profile.Monitors);

                var after = DisplayConfig.Capture();
                if (confirmMonitors != null && NeedsConfirmation(confirmMode, before, after) && !await confirmMonitors())
                {
                    DisplayConfig.Apply(before);
                    log(Loc.T("Disposición revertida."));
                    return false;
                }
                LastKnownGood.Save(after);
            }
        }

        if (profile.AudioOutput != null)
        {
            log(Loc.T("Esperando la salida de sonido '{0}'...", profile.AudioOutput));
            var output = await AudioOutputs.WaitFor(profile.AudioOutput, TimeSpan.FromSeconds(profile.AudioTimeoutSeconds), ct);
            if (output == null) throw new InvalidOperationException(Loc.T("No se encontró la salida de sonido '{0}'.", profile.AudioOutput));
            AudioOutputs.SetDefault(output.Id);
            log(Loc.T("Salida de sonido: {0}", output.Name));
        }

        foreach (var line in profile.Launch.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            // one broken extra action must not stop the rest of the profile
            try { await ExtraActions.RunAsync(line, log, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log(Loc.T("No se pudo ejecutar «{0}»: {1}", line, ex.Message)); }
        }

        if (profile.TvPower == "off")
        {
            log(Loc.T("Apagando la tele..."));
            using var tv = await LgTv.Connect(tvSettings, TimeSpan.FromSeconds(10), ct: ct);
            await tv.TurnOff(ct);
        }

        return true;
    }
}
