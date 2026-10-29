using MonitorTv.Core;
using System.Runtime.InteropServices;

namespace MonitorTv.App;

/// <summary>
/// Watches Xbox-style (XInput) controllers and runs an action when an exact button combination is held, so a profile can be
/// switched from the couch. Combinations are stored as XInput button masks.
/// </summary>
public sealed class ControllerListener : IDisposable
{
    private const int PollMs = 40;
    private const int HoldMs = 500;           // how long a combination must be held to fire
    private const int RecordStableMs = 800;   // how long a combination must stay unchanged while recording

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    // Ordinal 100 is XInputGetStateEx, the only one that also reports the Guide button.
    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern int GetStateEx(int user, out State state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int GetState(int user, out State state);

    /// <summary>Combinations that stay clear of Steam (it owns the Guide button and its chords) and of the one-button or menu-button
    /// presses games use: several buttons held together for half a second are very unlikely to happen by accident.</summary>
    public static readonly (string Label, ushort Mask)[] Presets =
    [
        ("LB + RB + Back + Start", 0x0330),
        ("L3 + R3 + Back", 0x00E0),
        ("LB + RB + L3 + R3", 0x03C0),
    ];

    public const ushort GuideMask = 0x0400;

    public static IReadOnlyList<(ushort Mask, string Name)> Buttons => ButtonNames;

    private static readonly (ushort Mask, string Name)[] ButtonNames =
    [
        (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
        (0x0100, "LB"), (0x0200, "RB"), (0x0040, "L3"), (0x0080, "R3"),
        (0x0020, "Back"), (0x0010, "Start"), (0x0400, "Guía"),
        (0x0001, "Arriba"), (0x0002, "Abajo"), (0x0004, "Izq."), (0x0008, "Der."),
    ];

    private readonly Dictionary<ushort, Action> _combos = [];
    private readonly object _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource<ushort>? _recording;

    public ControllerListener() => new Thread(Poll) { IsBackground = true, Name = "Controller listener" }.Start();

    public void Clear() { lock (_lock) _combos.Clear(); }

    public bool Register(ushort mask, Action action)
    {
        lock (_lock) return mask != 0 && _combos.TryAdd(mask, action);
    }

    /// <summary>Completes with the combination the user holds next (at least one button, steady for a moment).</summary>
    public Task<ushort> RecordAsync()
    {
        var tcs = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _recording = tcs;
        return tcs.Task;
    }

    public static string Describe(ushort mask) =>
        string.Join(" + ", ButtonNames.Where(b => (mask & b.Mask) != 0).Select(b => Loc.T(b.Name)));

    private static ushort ReadAll()
    {
        ushort all = 0;
        for (var user = 0; user < 4; user++)
        {
            State s;
            int result;
            try { result = GetStateEx(user, out s); }
            catch (EntryPointNotFoundException) { result = GetState(user, out s); }
            catch (DllNotFoundException) { return 0; }
            if (result == 0) all |= s.Buttons;
        }
        return all;
    }

    private void Poll()
    {
        ushort current = 0;
        var heldSince = DateTime.UtcNow;
        var fired = false;

        while (!_stop.IsCancellationRequested)
        {
            Thread.Sleep(PollMs);
            var now = ReadAll();
            if (now != current)
            {
                current = now;
                heldSince = DateTime.UtcNow;
                fired = false;
                continue;
            }
            if (current == 0) continue;

            var held = (DateTime.UtcNow - heldSince).TotalMilliseconds;

            TaskCompletionSource<ushort>? recording;
            lock (_lock) recording = _recording;
            if (recording != null)
            {
                if (held >= RecordStableMs)
                {
                    lock (_lock) _recording = null;
                    recording.TrySetResult(current);
                    fired = true; // do not also trigger a profile with the combination just recorded
                }
                continue;
            }

            if (fired || held < HoldMs) continue;
            Action? action;
            lock (_lock) _combos.TryGetValue(current, out action);
            if (action != null)
            {
                fired = true;
                try { action(); } catch { /* a failing profile must not stop the listener */ }
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}
