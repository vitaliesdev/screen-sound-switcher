using System.Runtime.InteropServices;
using System.Text;

namespace MonitorTv.Core.Monitors;

/// <summary>A monitor output as Windows numbers it right now, plus a stable device path that survives reboots.</summary>
public sealed record TargetRef(uint AdapterLow, int AdapterHigh, uint TargetId, string DevicePath);

/// <summary>
/// Windows renumbers graphics adapters (LUIDs) and sometimes outputs after a reboot or driver reset, which would make a
/// stored layout fail to apply. Each layout remembers its monitors' device paths so it can be re-pointed at today's numbers.
/// </summary>
public static class DisplayIdentity
{
    private const int PathSize = 72, ModeSize = 64;
    private const int PathSourceAdapter = 0, PathTargetAdapter = 20, PathTargetId = 28;
    private const int ModeId = 4, ModeAdapter = 8, ModeInfoType = 0;
    private const uint QDC_ALL_PATHS = 0x1;
    private const uint TypeSource = 1, TypeTarget = 2;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths, ref uint modeCount, IntPtr modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(byte[] requestPacket);

    /// <summary>Every monitor output Windows currently knows, active or not.</summary>
    public static List<TargetRef> CurrentTargets()
    {
        if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out var pathCount, out var modeCount) != 0) return [];
        var paths = Marshal.AllocHGlobal((int)pathCount * PathSize);
        var modes = Marshal.AllocHGlobal((int)modeCount * ModeSize);
        try
        {
            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return [];
            var bytes = new byte[(int)pathCount * PathSize];
            Marshal.Copy(paths, bytes, 0, bytes.Length);

            var seen = new HashSet<(uint, int, uint)>();
            var result = new List<TargetRef>();
            for (var i = 0; i < pathCount; i++)
            {
                var r = RefAt(bytes, i * PathSize);
                if (r != null && seen.Add((r.AdapterLow, r.AdapterHigh, r.TargetId))) result.Add(r);
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    /// <summary>Stores the device path of every monitor in a layout captured with today's numbering.</summary>
    public static DisplayConfig.Layout WithTargets(DisplayConfig.Layout layout)
    {
        var paths = Convert.FromBase64String(layout.Paths);
        var refs = new List<TargetRef>();
        for (var i = 0; i < layout.PathCount; i++)
            if (RefAt(paths, i * PathSize) is { } r) refs.Add(r);
        return refs.Count == layout.PathCount ? layout with { Targets = refs } : layout;
    }

    /// <summary>
    /// Re-points a stored layout at the adapters/outputs Windows uses now. Returns the layout unchanged when it has no
    /// device paths, nothing moved, or a monitor is not connected.
    /// </summary>
    public static DisplayConfig.Layout Refresh(DisplayConfig.Layout layout)
    {
        if (layout.Targets is not { Count: > 0 } saved) return layout;

        var current = CurrentTargets();
        var adapterMap = new Dictionary<ulong, ulong>();
        var targetMap = new Dictionary<(ulong, uint), (ulong, uint)>();
        var updated = new List<TargetRef>();

        foreach (var old in saved)
        {
            var now = current.FirstOrDefault(c => c.DevicePath.Equals(old.DevicePath, StringComparison.OrdinalIgnoreCase));
            if (now == null) { updated.Add(old); continue; }
            updated.Add(now);

            var oldKey = Key(old.AdapterLow, old.AdapterHigh);
            var newKey = Key(now.AdapterLow, now.AdapterHigh);
            if (oldKey != newKey) adapterMap[oldKey] = newKey;
            if (oldKey != newKey || old.TargetId != now.TargetId) targetMap[(oldKey, old.TargetId)] = (newKey, now.TargetId);
        }
        if (targetMap.Count == 0) return layout with { Targets = updated };

        var paths = Convert.FromBase64String(layout.Paths);
        var modes = Convert.FromBase64String(layout.Modes);

        for (var i = 0; i < layout.PathCount; i++)
        {
            var p = i * PathSize;
            var targetAdapter = ReadKey(paths, p + PathTargetAdapter);
            var targetId = BitConverter.ToUInt32(paths, p + PathTargetId);
            if (targetMap.TryGetValue((targetAdapter, targetId), out var to))
            {
                WriteKey(paths, p + PathTargetAdapter, to.Item1);
                BitConverter.TryWriteBytes(paths.AsSpan(p + PathTargetId), to.Item2);
            }
            if (adapterMap.TryGetValue(ReadKey(paths, p + PathSourceAdapter), out var sourceAdapter))
                WriteKey(paths, p + PathSourceAdapter, sourceAdapter);
        }

        for (var i = 0; i < layout.ModeCount; i++)
        {
            var m = i * ModeSize;
            var type = BitConverter.ToUInt32(modes, m + ModeInfoType);
            var adapter = ReadKey(modes, m + ModeAdapter);
            if (type == TypeTarget && targetMap.TryGetValue((adapter, BitConverter.ToUInt32(modes, m + ModeId)), out var to))
            {
                WriteKey(modes, m + ModeAdapter, to.Item1);
                BitConverter.TryWriteBytes(modes.AsSpan(m + ModeId), to.Item2);
            }
            else if (type == TypeSource && adapterMap.TryGetValue(adapter, out var newAdapter))
            {
                WriteKey(modes, m + ModeAdapter, newAdapter);
            }
        }

        return layout with { Paths = Convert.ToBase64String(paths), Modes = Convert.ToBase64String(modes), Targets = updated };
    }

    /// <summary>Names of the layout's monitors that Windows does not currently report (for a clear error message).</summary>
    public static List<string> MissingMonitors(DisplayConfig.Layout layout)
    {
        if (layout.Targets is not { Count: > 0 } saved) return [];
        var current = CurrentTargets();
        return saved.Where(s => !current.Any(c => c.DevicePath.Equals(s.DevicePath, StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.DevicePath).ToList();
    }

    private static TargetRef? RefAt(byte[] paths, int pathOffset)
    {
        var path = DevicePath(paths, pathOffset);
        if (path == null) return null;
        return new TargetRef(BitConverter.ToUInt32(paths, pathOffset + PathTargetAdapter),
            BitConverter.ToInt32(paths, pathOffset + PathTargetAdapter + 4),
            BitConverter.ToUInt32(paths, pathOffset + PathTargetId), path);
    }

    // DISPLAYCONFIG_TARGET_DEVICE_NAME: header (type, size, adapter, id) then flags, tech, edid ids, connector, name[64], path[128]
    private static string? DevicePath(byte[] paths, int pathOffset)
    {
        const int size = 420, pathField = 164;
        var packet = new byte[size];
        BitConverter.TryWriteBytes(packet.AsSpan(0), 2);
        BitConverter.TryWriteBytes(packet.AsSpan(4), size);
        Array.Copy(paths, pathOffset + PathTargetAdapter, packet, 8, 8);
        Array.Copy(paths, pathOffset + PathTargetId, packet, 16, 4);
        if (DisplayConfigGetDeviceInfo(packet) != 0) return null;
        var text = Encoding.Unicode.GetString(packet, pathField, 256).TrimEnd('\0');
        return text.Length == 0 ? null : text;
    }

    private static ulong Key(uint low, int high) => low | ((ulong)(uint)high << 32);
    private static ulong ReadKey(byte[] b, int o) => BitConverter.ToUInt64(b, o);
    private static void WriteKey(byte[] b, int o, ulong key) => BitConverter.TryWriteBytes(b.AsSpan(o), key);
}
