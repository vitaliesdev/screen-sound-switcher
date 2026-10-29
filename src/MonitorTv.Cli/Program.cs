using MonitorTv.Core.Audio;
using MonitorTv.Core.Devices;
using MonitorTv.Core.Monitors;
using MonitorTv.Core.Profiles;
using MonitorTv.Core.Tv;

var store = new ProfileStore();

string Usage() => """
    MonitorTv CLI (prototype)
      audio list                      list playback outputs (* = default)
      audio set <name part>           make the matching output the default
      monitors check                  capture the current layout and ask Windows to validate it (changes nothing)
      profile list                    list profiles
      profile save <name>             store the current monitor layout in a profile
      profile audio <name> <name part>  set the audio output a profile switches to
      profile run <name>              apply a profile (TV, monitors, audio, programs)
      profile tv <name> <on|off|-> <HDMI_1..4|TV|->   TV power/input for a profile ("-" = unchanged)
      profile launch <name> <program or uri>          add a program/URI the profile starts
      tv config <ip> <mac>            set the LG TV address
      tv pair | info | on | off | input <HDMI_1..4|TV>
    """;

try
{
    switch (args)
    {
        case ["audio", "list"]:
            foreach (var o in AudioOutputs.List())
                Console.WriteLine($"{(o.IsDefault ? '*' : ' ')} {o.Name}");
            break;

        case ["arctis", "devices"]:
            foreach (var h in HidSharp.DeviceList.Local.GetHidDevices(0x1038))
                Console.WriteLine($"pid=0x{h.ProductID:X4} in={h.GetMaxInputReportLength()} out={h.GetMaxOutputReportLength()} feat={h.GetMaxFeatureReportLength()}  {h.DevicePath}");
            break;

        case ["arctis", "status"]:
            var one = ArctisHeadset.Read();
            Console.WriteLine($"{one.State}  battery={one.BatteryPercent}  ({one.Detail})");
            break;

        case ["arctis", "watch", var arctisSeconds]:
            var untilArctis = DateTime.Now.AddSeconds(int.Parse(arctisSeconds));
            ArctisHeadset.State? lastState = null;
            var arctisMonitor = new ArctisHeadset.Monitor();
            while (DateTime.Now < untilArctis)
            {
                var r = arctisMonitor.Read();
                if (r.State != lastState)
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {r.State}  battery={r.BatteryPercent}  ({r.Detail})");
                lastState = r.State;
                await Task.Delay(1000);
            }
            Console.WriteLine("done.");
            break;

        case ["audio", "watch", var seconds]:
            // Prints every change in the active playback outputs and the default one, to see what Windows reports.
            var seen = AudioOutputs.List().ToDictionary(o => o.Id);
            var lastDefault = seen.Values.FirstOrDefault(o => o.IsDefault)?.Name;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] watching: {string.Join(" | ", seen.Values.Select(o => o.Name))}  (default: {lastDefault})");
            var until = DateTime.Now.AddSeconds(int.Parse(seconds));
            while (DateTime.Now < until)
            {
                await Task.Delay(300);
                var now = AudioOutputs.List().ToDictionary(o => o.Id);
                foreach (var added in now.Values.Where(o => !seen.ContainsKey(o.Id)))
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] APPEARED : {added.Name}");
                foreach (var removed in seen.Values.Where(o => !now.ContainsKey(o.Id)))
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DISAPPEARED: {removed.Name}");
                var def = now.Values.FirstOrDefault(o => o.IsDefault)?.Name;
                if (def != lastDefault) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] default changed: {lastDefault} -> {def}");
                seen = now;
                lastDefault = def;
            }
            Console.WriteLine("done.");
            break;

        case ["audio", "set", var part]:
            var found = AudioOutputs.Find(part) ?? throw new InvalidOperationException($"No output matches '{part}'.");
            AudioOutputs.SetDefault(found.Id);
            Console.WriteLine($"Default output: {found.Name}");
            break;

        case ["monitors", "check"]:
            var layout = DisplayConfig.Capture();
            Console.WriteLine($"Captured {layout.PathCount} paths, {layout.ModeCount} modes.");
            DisplayConfig.Validate(layout);
            Console.WriteLine("Windows accepts this layout.");
            break;

        case ["profile", "show", var name]:
            var shown = store.Load(name);
            foreach (var b in shown.Monitors == null ? [] : LayoutEditor.Read(shown.Monitors))
                Console.WriteLine($"{b.Name,-20} {b.Width}x{b.Height} at ({b.X},{b.Y})");
            break;

        case ["profile", "merge", var target, var first, var second]:
            var merged = LayoutEditor.Merge(store.Load(first).Monitors!, store.Load(second).Monitors!);
            DisplayConfig.Validate(merged);
            var mp = File.Exists(Path.Combine(store.Root, target + ".json")) ? store.Load(target) : new Profile { Name = target };
            mp.Monitors = merged;
            store.Save(mp);
            Console.WriteLine($"'{target}' = {first} + {second} (validated by Windows).");
            break;

        case ["profile", "place", var name, var monitor, var px, var py]:
            var pp = store.Load(name);
            var box = LayoutEditor.Read(pp.Monitors!).First(m => m.Name.Contains(monitor, StringComparison.OrdinalIgnoreCase));
            var all = LayoutEditor.Read(pp.Monitors!).ToDictionary(m => m.ModeIndex, m => (m.X, m.Y));
            all[box.ModeIndex] = (int.Parse(px), int.Parse(py));
            pp.Monitors = LayoutEditor.WithPositions(pp.Monitors!, all);
            DisplayConfig.Validate(pp.Monitors);
            store.Save(pp);
            Console.WriteLine($"{box.Name} at ({px},{py}) in '{name}' (validated by Windows).");
            break;

        case ["profile", "hotkey", var name, var hotkey]:
            var ph = store.Load(name);
            ph.Hotkey = hotkey == "-" ? null : hotkey;
            store.Save(ph);
            Console.WriteLine($"'{name}': hotkey {ph.Hotkey ?? "none"}.");
            break;

        case ["profile", "validate", var name]:
            var pv = store.Load(name);
            var gone = DisplayIdentity.MissingMonitors(pv.Monitors!);
            if (gone.Count > 0) throw new InvalidOperationException($"{gone.Count} monitor(s) not connected.");
            DisplayConfig.Validate(pv.Monitors!);
            Console.WriteLine($"'{name}': Windows accepts it.");
            break;

        case ["actions", "parse", var line]:
            Console.WriteLine(ExtraActions.Parse(line)?.ToString() ?? "(empty)");
            break;

        case ["profile", "migrate"]:
            Console.WriteLine($"{store.MigrateTargets()} profile(s) updated with monitor identities.");
            break;

        case ["monitors", "available"]:
            foreach (var m in LayoutBuilder.Available())
                Console.WriteLine($"{(m.Active ? "on " : "off")} {m.Name} {m.Width}x{m.Height} at ({m.X},{m.Y})  target {m.Target.TargetId}");
            break;

        case ["monitors", "design", .. var wantedNames]:
            // builds a layout with the named monitors side by side (all when none are named) and asks Windows to validate it; applies nothing
            var avail = LayoutBuilder.Available().Where(m => wantedNames.Length == 0 || wantedNames.Any(n => m.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();
            var x = 0;
            var built = LayoutBuilder.Build(avail.Select(m => { var item = (m, x, 0); x += m.Width; return item; }).ToList());
            foreach (var b in LayoutEditor.Read(built)) Console.WriteLine($"{b.Name} {b.Width}x{b.Height} at ({b.X},{b.Y})");
            DisplayConfig.Validate(built);
            Console.WriteLine("Windows accepts it.");
            break;

        case ["monitors", "needs-confirm", var confirmMode, .. var confirmNames]:
            // would changing from the current layout to a layout with these monitors ask for confirmation? (applies nothing)
            var current = DisplayConfig.Capture();
            var wantedMonitors = LayoutBuilder.Available().Where(m => confirmNames.Any(n => m.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();
            var cx = 0;
            var next = LayoutBuilder.Build(wantedMonitors.Select(m => { var item = (m, cx, 0); cx += m.Width; return item; }).ToList());
            Console.WriteLine($"{confirmMode}: current -> [{string.Join(", ", wantedMonitors.Select(m => m.Name))}] asks: {ProfileRunner.NeedsConfirmation(confirmMode, current, next)}");
            break;

        case ["monitors", "guard", var guardProfile, .. var guardFlags]:
            // simulates "a monitor disappeared" (or, with "grow", appeared) and shows what the safety net would do; with "apply" it does it
            var intendedLayout = store.Load(guardProfile).Monitors!;
            var nowLayout = DisplayConfig.Capture();
            var after = DisplayGuard.ConnectedPaths();
            var before = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
            if (guardFlags.Contains("grow")) after.Add(@"\\?\fake-monitor"); else before.Add(@"\\?\fake-tv");
            var plan = DisplayGuard.Plan(intendedLayout, nowLayout, before, after);
            Console.WriteLine(plan == null ? "nothing to do" : "restore: " + string.Join(", ", LayoutEditor.Read(plan).Select(b => $"{b.Name} at ({b.X},{b.Y})")));
            if (plan != null && guardFlags.Contains("apply"))
            {
                DisplayConfig.Validate(plan);
                DisplayConfig.Apply(plan);
                Console.WriteLine("applied: " + string.Join(", ", LayoutEditor.Read(DisplayConfig.Capture()).Select(b => b.Name)));
            }
            break;

        case ["monitors", "identity"]:
            foreach (var t in DisplayIdentity.CurrentTargets())
                Console.WriteLine($"adapter {t.AdapterLow}:{t.AdapterHigh} target {t.TargetId}  {t.DevicePath}");
            break;

        case ["profile", "list"]:
            foreach (var name in store.List()) Console.WriteLine(name);
            break;

        case ["profile", "save", var name]:
            var saved = store.SaveCurrentMonitors(name);
            Console.WriteLine($"Saved '{saved.Name}' in {store.Root}");
            break;

        case ["profile", "audio", var name, var part]:
            var p = store.Load(name);
            p.AudioOutput = part;
            store.Save(p);
            Console.WriteLine($"'{name}' will switch audio to '{part}'.");
            break;

        case ["profile", "run", var name]:
            await ProfileRunner.Run(store.Load(name), Console.WriteLine);
            break;

        case ["tv", "config", var ip, var mac]:
            var cfg = TvSettings.Load();
            cfg.Ip = ip;
            cfg.Mac = mac;
            cfg.Save();
            Console.WriteLine($"TV set to {ip} ({mac}).");
            break;

        case ["tv", "pair"]:
            Console.WriteLine("Accept the prompt on the TV...");
            using (await LgTv.Connect(TvSettings.Load(), TimeSpan.FromSeconds(10), pairing: true)) { }
            Console.WriteLine("Paired.");
            break;

        case ["tv", "info"]:
            using (var tv = await LgTv.Connect(TvSettings.Load(), TimeSpan.FromSeconds(10)))
                foreach (var line in await tv.Info()) Console.WriteLine(line);
            break;

        case ["tv", "input", var input]:
            using (var tv = await LgTv.Connect(TvSettings.Load(), TimeSpan.FromSeconds(30)))
                await tv.SetInput(input);
            Console.WriteLine($"TV input: {input}");
            break;

        case ["tv", "on"]:
            LgTv.WakeOnLan(TvSettings.Load().Mac);
            Console.WriteLine("Wake-on-LAN sent.");
            break;

        case ["tv", "off"]:
            using (var tv = await LgTv.Connect(TvSettings.Load(), TimeSpan.FromSeconds(10)))
                await tv.TurnOff();
            Console.WriteLine("TV off.");
            break;

        case ["profile", "tv", var name, var power, var input]:
            var pt = store.Load(name);
            pt.TvPower = power == "-" ? null : power;
            pt.TvInput = input == "-" ? null : input;
            store.Save(pt);
            Console.WriteLine($"'{name}': TV power {pt.TvPower ?? "unchanged"}, input {pt.TvInput ?? "unchanged"}.");
            break;

        case ["profile", "launch", var name, var command]:
            var pl = store.Load(name);
            pl.Launch.Add(command);
            store.Save(pl);
            Console.WriteLine($"'{name}' will launch {command}.");
            break;

        default:
            Console.WriteLine(Usage());
            return 1;
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 1;
}
