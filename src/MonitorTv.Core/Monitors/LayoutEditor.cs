using System.Runtime.InteropServices;
using System.Text;

namespace MonitorTv.Core.Monitors;

/// <summary>Reads and edits monitor positions inside a captured <see cref="DisplayConfig.Layout"/>, and merges layouts.</summary>
public static class LayoutEditor
{
    private const int PathSize = 72, ModeSize = 64;

    // DISPLAYCONFIG_PATH_INFO offsets
    private const int PathSourceModeIdx = 12, PathTargetId = 28, PathTargetModeIdx = 32;
    private const int PathSourceId = 8;
    // DISPLAYCONFIG_MODE_INFO offsets (source mode)
    private const int ModeInfoType = 0, SourceWidth = 16, SourceHeight = 20, SourceX = 28, SourceY = 32;
    private const uint ModeTypeSource = 1;
    private const uint InvalidIdx = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(byte[] requestPacket);

    /// <summary>One active monitor of a layout, positioned in virtual desktop coordinates.</summary>
    public sealed record MonitorBox(int ModeIndex, uint TargetId, string Name, int Width, int Height, int X, int Y);

    public static List<MonitorBox> Read(DisplayConfig.Layout layout)
    {
        var paths = Convert.FromBase64String(layout.Paths);
        var modes = Convert.FromBase64String(layout.Modes);
        var boxes = new List<MonitorBox>();

        for (var i = 0; i < layout.PathCount; i++)
        {
            var p = i * PathSize;
            var sourceIdx = BitConverter.ToUInt32(paths, p + PathSourceModeIdx);
            if (sourceIdx == InvalidIdx || sourceIdx >= layout.ModeCount) continue;
            var m = (int)sourceIdx * ModeSize;
            if (BitConverter.ToUInt32(modes, m + ModeInfoType) != ModeTypeSource) continue;

            var targetId = BitConverter.ToUInt32(paths, p + PathTargetId);
            boxes.Add(new MonitorBox((int)sourceIdx, targetId, TargetName(paths, p),
                BitConverter.ToInt32(modes, m + SourceWidth), BitConverter.ToInt32(modes, m + SourceHeight),
                BitConverter.ToInt32(modes, m + SourceX), BitConverter.ToInt32(modes, m + SourceY)));
        }
        return boxes;
    }

    /// <summary>Returns a copy of the layout with new top-left positions, keyed by source mode index.</summary>
    public static DisplayConfig.Layout WithPositions(DisplayConfig.Layout layout, IReadOnlyDictionary<int, (int X, int Y)> positions)
    {
        var modes = Convert.FromBase64String(layout.Modes);
        foreach (var (index, (x, y)) in positions)
        {
            BitConverter.TryWriteBytes(modes.AsSpan(index * ModeSize + SourceX), x);
            BitConverter.TryWriteBytes(modes.AsSpan(index * ModeSize + SourceY), y);
        }
        return layout with { Modes = Convert.ToBase64String(modes) };
    }

    /// <summary>
    /// Combines two single-purpose layouts (e.g. "AOC only" + "Alienware only") into one with both monitors active.
    /// The second layout's monitors are placed to the right of the first until you arrange them.
    /// </summary>
    public static DisplayConfig.Layout Merge(DisplayConfig.Layout a, DisplayConfig.Layout b)
    {
        var pathsA = Convert.FromBase64String(a.Paths);
        var pathsB = Convert.FromBase64String(b.Paths);
        var modesA = Convert.FromBase64String(a.Modes);
        var modesB = Convert.FromBase64String(b.Modes);

        var usedSources = Enumerable.Range(0, (int)a.PathCount).Select(i => BitConverter.ToUInt32(pathsA, i * PathSize + PathSourceId)).ToHashSet();
        for (var i = 0; i < b.PathCount; i++)
            if (usedSources.Contains(BitConverter.ToUInt32(pathsB, i * PathSize + PathSourceId)))
                throw new InvalidOperationException("Both layouts use the same display source; they cannot be merged directly.");

        for (var i = 0; i < b.PathCount; i++)
        {
            var p = i * PathSize;
            ShiftIndex(pathsB, p + PathSourceModeIdx, a.ModeCount);
            ShiftIndex(pathsB, p + PathTargetModeIdx, a.ModeCount);
        }

        var rightEdge = Read(a).Max(box => box.X + box.Width);
        for (var m = 0; m < b.ModeCount; m++)
        {
            var o = m * ModeSize;
            if (BitConverter.ToUInt32(modesB, o + ModeInfoType) != ModeTypeSource) continue;
            BitConverter.TryWriteBytes(modesB.AsSpan(o + SourceX), rightEdge + BitConverter.ToInt32(modesB, o + SourceX));
        }

        IReadOnlyList<TargetRef>? targets = a.Targets != null && b.Targets != null ? [.. a.Targets, .. b.Targets] : null;
        return new DisplayConfig.Layout(
            a.PathCount + b.PathCount, a.ModeCount + b.ModeCount,
            Convert.ToBase64String([.. pathsA, .. pathsB]), Convert.ToBase64String([.. modesA, .. modesB]), targets);
    }

    private static void ShiftIndex(byte[] paths, int offset, uint by)
    {
        var value = BitConverter.ToUInt32(paths, offset);
        if (value != InvalidIdx) BitConverter.TryWriteBytes(paths.AsSpan(offset), value + by);
    }

    // DISPLAYCONFIG_TARGET_DEVICE_NAME: 20-byte header (type, size, adapter LUID, id), then 420 - 20 bytes
    private static string TargetName(byte[] paths, int pathOffset)
    {
        const int size = 420, nameOffset = 36;
        var packet = new byte[size];
        BitConverter.TryWriteBytes(packet.AsSpan(0), 2);          // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
        BitConverter.TryWriteBytes(packet.AsSpan(4), size);
        Array.Copy(paths, pathOffset + 20, packet, 8, 8);         // target adapterId
        Array.Copy(paths, pathOffset + PathTargetId, packet, 16, 4);

        if (DisplayConfigGetDeviceInfo(packet) != 0) return Loc.T("Monitor {0}", BitConverter.ToUInt32(paths, pathOffset + PathTargetId));
        var name = Encoding.Unicode.GetString(packet, nameOffset, 128).TrimEnd('\0');
        return name.Length == 0 ? Loc.T("Monitor {0}", BitConverter.ToUInt32(paths, pathOffset + PathTargetId)) : name;
    }
}
