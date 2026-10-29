using System.Runtime.InteropServices;
using System.Text;

namespace MonitorTv.Core.Monitors;

/// <summary>
/// Builds a monitor layout from the monitors Windows reports, active or not, without applying anything: the data behind
/// designing a profile visually. A monitor that is on keeps its current timing; one that is off gets its preferred
/// resolution and Windows picks the rest when the profile is applied.
/// </summary>
public static class LayoutBuilder
{
    private const int PathSize = 72, ModeSize = 64;
    private const uint QDC_ALL_PATHS = 0x1, QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const uint InvalidIdx = 0xFFFFFFFF, TypeSource = 1, TypeTarget = 2;
    private const uint PathActive = 1;

    // DISPLAYCONFIG_PATH_INFO offsets
    private const int SrcAdapter = 0, SrcId = 8, SrcModeIdx = 12, TgtAdapter = 20, TgtId = 28, TgtModeIdx = 32;
    private const int Rotation = 40, Scaling = 44, TargetAvailable = 60, PathFlags = 68;
    // DISPLAYCONFIG_MODE_INFO offsets
    private const int ModeType = 0, ModeId = 4, ModeAdapter = 8, SrcWidth = 16, SrcHeight = 20, SrcPixelFormat = 24, SrcX = 28, SrcY = 32;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths, ref uint modeCount, IntPtr modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(byte[] requestPacket);

    /// <summary>A monitor Windows can use right now; <see cref="X"/>/<see cref="Y"/> are where it sits when <see cref="Active"/>.</summary>
    public sealed record Monitor(TargetRef Target, string Name, int Width, int Height, bool Active, int X, int Y);

    private sealed record Snapshot(byte[] Paths, byte[] Modes, int PathCount);

    private static Snapshot Query(uint flags)
    {
        if (GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount) != 0) return new([], [], 0);
        var paths = Marshal.AllocHGlobal((int)pathCount * PathSize);
        var modes = Marshal.AllocHGlobal((int)modeCount * ModeSize);
        try
        {
            if (QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return new([], [], 0);
            var p = new byte[(int)pathCount * PathSize];
            var m = new byte[(int)modeCount * ModeSize];
            Marshal.Copy(paths, p, 0, p.Length);
            Marshal.Copy(modes, m, 0, m.Length);
            return new Snapshot(p, m, (int)pathCount);
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    private static ulong Key(byte[] b, int o) => BitConverter.ToUInt64(b, o);

    /// <summary>Every connected monitor once, in a stable order, with where it is now when it is on.</summary>
    public static List<Monitor> Available()
    {
        var all = Query(QDC_ALL_PATHS);
        var active = Query(QDC_ONLY_ACTIVE_PATHS);
        var result = new List<Monitor>();
        var seen = new HashSet<(ulong, uint)>();

        for (var i = 0; i < all.PathCount; i++)
        {
            var p = i * PathSize;
            if (BitConverter.ToUInt32(all.Paths, p + TargetAvailable) == 0) continue;
            var adapter = Key(all.Paths, p + TgtAdapter);
            var id = BitConverter.ToUInt32(all.Paths, p + TgtId);
            if (!seen.Add((adapter, id))) continue;

            var info = DeviceInfo(all.Paths, p);
            if (info == null) continue;
            var (name, devicePath) = info.Value;
            var target = new TargetRef(BitConverter.ToUInt32(all.Paths, p + TgtAdapter), BitConverter.ToInt32(all.Paths, p + TgtAdapter + 4), id, devicePath);

            var on = FindActive(active, adapter, id);
            if (on is { } a)
            {
                var m = a.SourceMode * ModeSize;
                result.Add(new Monitor(target, name, BitConverter.ToInt32(active.Modes, m + SrcWidth), BitConverter.ToInt32(active.Modes, m + SrcHeight),
                    true, BitConverter.ToInt32(active.Modes, m + SrcX), BitConverter.ToInt32(active.Modes, m + SrcY)));
            }
            else
            {
                var (w, h) = PreferredSize(all.Paths, p);
                result.Add(new Monitor(target, name, w, h, false, 0, 0));
            }
        }
        return result.OrderBy(m => m.Target.DevicePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (int Path, int SourceMode, int TargetMode)? FindActive(Snapshot active, ulong adapter, uint id)
    {
        for (var i = 0; i < active.PathCount; i++)
        {
            var p = i * PathSize;
            if (Key(active.Paths, p + TgtAdapter) != adapter || BitConverter.ToUInt32(active.Paths, p + TgtId) != id) continue;
            var source = BitConverter.ToUInt32(active.Paths, p + SrcModeIdx);
            if (source == InvalidIdx || (source + 1) * ModeSize > active.Modes.Length) return null;
            var target = BitConverter.ToUInt32(active.Paths, p + TgtModeIdx);
            return (i, (int)source, target == InvalidIdx || (target + 1) * ModeSize > active.Modes.Length ? -1 : (int)target);
        }
        return null;
    }

    /// <summary>
    /// A layout with the given monitors on, each at its top-left position (virtual desktop pixels). The monitor at 0,0 is the
    /// primary one. Nothing is applied; <see cref="DisplayConfig.Validate"/> asks Windows whether it would work.
    /// </summary>
    public static DisplayConfig.Layout Build(IReadOnlyList<(Monitor Monitor, int X, int Y)> wanted, DisplayConfig.Layout? basis = null)
    {
        if (wanted.Count == 0) throw new InvalidOperationException(Loc.T("Elige al menos una pantalla."));

        var all = Query(QDC_ALL_PATHS);
        // Monitors already in the basis layout (a profile being edited) keep its resolution and refresh rate; the rest of
        // the monitors that are on keep their current timing.
        var sources = new List<Snapshot>();
        if (basis != null)
        {
            var refreshed = DisplayIdentity.Refresh(basis);
            sources.Add(new Snapshot(Convert.FromBase64String(refreshed.Paths), Convert.FromBase64String(refreshed.Modes), (int)refreshed.PathCount));
        }
        sources.Add(Query(QDC_ONLY_ACTIVE_PATHS));

        var paths = new List<byte>();
        var modes = new List<byte>();
        var usedSources = new HashSet<(ulong, uint)>();

        // monitors with a known timing keep their path, so claim their source ids first
        var keep = wanted.Select(w =>
        {
            var adapter = Key(AdapterBytes(w.Monitor.Target), 0);
            foreach (var s in sources)
                if (FindActive(s, adapter, w.Monitor.Target.TargetId) is { } found) return (w, on: ((Snapshot, (int Path, int SourceMode, int TargetMode))?)(s, found));
            return (w, on: ((Snapshot, (int Path, int SourceMode, int TargetMode))?)null);
        }).ToList();
        foreach (var (_, on) in keep)
            if (on is { } hit) usedSources.Add((Key(hit.Item1.Paths, hit.Item2.Path * PathSize + SrcAdapter), BitConverter.ToUInt32(hit.Item1.Paths, hit.Item2.Path * PathSize + SrcId)));

        var count = 0;
        foreach (var (w, on) in keep)
        {
            byte[] path;
            if (on is { } known)
            {
                var (active, a) = known;
                path = active.Paths.AsSpan(a.Path * PathSize, PathSize).ToArray();
                var sourceMode = active.Modes.AsSpan(a.SourceMode * ModeSize, ModeSize).ToArray();
                var targetIndex = InvalidIdx;
                if (a.TargetMode >= 0)
                {
                    targetIndex = (uint)(modes.Count / ModeSize);
                    modes.AddRange(active.Modes.AsSpan(a.TargetMode * ModeSize, ModeSize).ToArray());
                }
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcX), w.X);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcY), w.Y);
                BitConverter.TryWriteBytes(path.AsSpan(SrcModeIdx), (uint)(modes.Count / ModeSize));
                BitConverter.TryWriteBytes(path.AsSpan(TgtModeIdx), targetIndex);
                modes.AddRange(sourceMode);
            }
            else
            {
                var found = FindTemplate(all, w.Monitor.Target, usedSources)
                    ?? throw new InvalidOperationException(Loc.T("Windows no tiene una salida libre para {0}.", w.Monitor.Name));
                path = found;
                var sourceAdapter = Key(path, SrcAdapter);
                var sourceId = BitConverter.ToUInt32(path, SrcId);
                usedSources.Add((sourceAdapter, sourceId));

                var sourceMode = new byte[ModeSize];
                BitConverter.TryWriteBytes(sourceMode.AsSpan(ModeType), TypeSource);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(ModeId), sourceId);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(ModeAdapter), sourceAdapter);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcWidth), w.Monitor.Width);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcHeight), w.Monitor.Height);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcPixelFormat), 4);   // DISPLAYCONFIG_PIXELFORMAT_32BPP
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcX), w.X);
                BitConverter.TryWriteBytes(sourceMode.AsSpan(SrcY), w.Y);

                BitConverter.TryWriteBytes(path.AsSpan(SrcModeIdx), (uint)(modes.Count / ModeSize));
                BitConverter.TryWriteBytes(path.AsSpan(TgtModeIdx), InvalidIdx);   // Windows chooses the timing
                BitConverter.TryWriteBytes(path.AsSpan(Rotation), 1u);
                BitConverter.TryWriteBytes(path.AsSpan(Scaling), 1u);
                BitConverter.TryWriteBytes(path.AsSpan(PathFlags), PathActive);
                modes.AddRange(sourceMode);
            }
            paths.AddRange(path);
            count++;
        }

        var layout = new DisplayConfig.Layout((uint)count, (uint)(modes.Count / ModeSize), Convert.ToBase64String([.. paths]), Convert.ToBase64String([.. modes]));
        return DisplayIdentity.WithTargets(layout);
    }

    private static byte[] AdapterBytes(TargetRef t)
    {
        var b = new byte[8];
        BitConverter.TryWriteBytes(b.AsSpan(0), t.AdapterLow);
        BitConverter.TryWriteBytes(b.AsSpan(4), t.AdapterHigh);
        return b;
    }

    /// <summary>A path of the monitor whose source (video output on the graphics card) is not taken yet.</summary>
    private static byte[]? FindTemplate(Snapshot all, TargetRef target, HashSet<(ulong, uint)> usedSources)
    {
        var adapter = Key(AdapterBytes(target), 0);
        for (var i = 0; i < all.PathCount; i++)
        {
            var p = i * PathSize;
            if (Key(all.Paths, p + TgtAdapter) != adapter || BitConverter.ToUInt32(all.Paths, p + TgtId) != target.TargetId) continue;
            if (usedSources.Contains((Key(all.Paths, p + SrcAdapter), BitConverter.ToUInt32(all.Paths, p + SrcId)))) continue;
            return all.Paths.AsSpan(p, PathSize).ToArray();
        }
        return null;
    }

    // DISPLAYCONFIG_TARGET_DEVICE_NAME: header(20) flags tech edid ids connector, friendly name[64] at 36, device path[128] at 164
    private static (string Name, string DevicePath)? DeviceInfo(byte[] paths, int pathOffset)
    {
        const int size = 420;
        var packet = new byte[size];
        BitConverter.TryWriteBytes(packet.AsSpan(0), 2);
        BitConverter.TryWriteBytes(packet.AsSpan(4), size);
        Array.Copy(paths, pathOffset + TgtAdapter, packet, 8, 8);
        Array.Copy(paths, pathOffset + TgtId, packet, 16, 4);
        if (DisplayConfigGetDeviceInfo(packet) != 0) return null;
        var path = Encoding.Unicode.GetString(packet, 164, 256).TrimEnd('\0');
        if (path.Length == 0) return null;
        var name = Encoding.Unicode.GetString(packet, 36, 128).TrimEnd('\0');
        return (name.Length == 0 ? Loc.T("Monitor {0}", BitConverter.ToUInt32(paths, pathOffset + TgtId)) : name, path);
    }

    // DISPLAYCONFIG_TARGET_PREFERRED_MODE: header(20), width, height, then the 8-aligned signal info (48 bytes)
    private static (int Width, int Height) PreferredSize(byte[] paths, int pathOffset)
    {
        const int size = 80;
        var packet = new byte[size];
        BitConverter.TryWriteBytes(packet.AsSpan(0), 3);
        BitConverter.TryWriteBytes(packet.AsSpan(4), size);
        Array.Copy(paths, pathOffset + TgtAdapter, packet, 8, 8);
        Array.Copy(paths, pathOffset + TgtId, packet, 16, 4);
        if (DisplayConfigGetDeviceInfo(packet) != 0) return (1920, 1080);
        var w = BitConverter.ToInt32(packet, 20);
        var h = BitConverter.ToInt32(packet, 24);
        return w > 0 && h > 0 ? (w, h) : (1920, 1080);
    }
}
