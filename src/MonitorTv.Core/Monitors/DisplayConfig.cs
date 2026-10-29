using System.Runtime.InteropServices;
using System.Text.Json;

namespace MonitorTv.Core.Monitors;

/// <summary>
/// Saves and restores the Windows display topology (which monitors are on, their
/// position, resolution, refresh rate) through the CCD API. Paths/modes are kept as
/// raw buffers so no field of the native structs is lost.
/// </summary>
public static class DisplayConfig
{
    // sizeof(DISPLAYCONFIG_PATH_INFO) and sizeof(DISPLAYCONFIG_MODE_INFO)
    private const int PathSize = 72;
    private const int ModeSize = 64;

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

    private const uint SDC_VALIDATE = 0x40;
    private const uint SDC_APPLY = 0x80;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    private const uint SDC_SAVE_TO_DATABASE = 0x200;
    private const uint SDC_ALLOW_CHANGES = 0x400;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths,
        ref uint modeCount, IntPtr modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, IntPtr paths, uint modeCount, IntPtr modes, uint flags);

    public sealed record Layout(uint PathCount, uint ModeCount, string Paths, string Modes, IReadOnlyList<TargetRef>? Targets = null);

    public static Layout Capture()
    {
        Check(GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount), "GetDisplayConfigBufferSizes");

        var paths = Marshal.AllocHGlobal((int)pathCount * PathSize);
        var modes = Marshal.AllocHGlobal((int)modeCount * ModeSize);
        try
        {
            Check(QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero), "QueryDisplayConfig");
            return DisplayIdentity.WithTargets(new Layout(pathCount, modeCount, ToBase64(paths, (int)pathCount * PathSize), ToBase64(modes, (int)modeCount * ModeSize)));
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    /// <summary>Asks Windows whether the layout is applicable without changing anything.</summary>
    public static void Validate(Layout layout) => Set(DisplayIdentity.Refresh(layout), SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES);

    public static void Apply(Layout layout) =>
        Set(DisplayIdentity.Refresh(layout), SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES);

    public static void Save(string file, Layout layout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Layout Load(string file) =>
        JsonSerializer.Deserialize<Layout>(File.ReadAllText(file)) ?? throw new InvalidDataException($"Invalid monitor layout: {file}");

    private static void Set(Layout layout, uint flags)
    {
        var pathBytes = Convert.FromBase64String(layout.Paths);
        var modeBytes = Convert.FromBase64String(layout.Modes);
        if (pathBytes.Length != layout.PathCount * PathSize || modeBytes.Length != layout.ModeCount * ModeSize)
            throw new InvalidDataException("Monitor layout does not match the expected native struct sizes.");

        var paths = Marshal.AllocHGlobal(pathBytes.Length);
        var modes = Marshal.AllocHGlobal(modeBytes.Length);
        try
        {
            Marshal.Copy(pathBytes, 0, paths, pathBytes.Length);
            Marshal.Copy(modeBytes, 0, modes, modeBytes.Length);
            Check(SetDisplayConfig(layout.PathCount, paths, layout.ModeCount, modes, flags), "SetDisplayConfig");
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
    }

    private static string ToBase64(IntPtr ptr, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return Convert.ToBase64String(bytes);
    }

    private static void Check(int result, string api)
    {
        if (result != 0)
            throw new InvalidOperationException(Loc.T("{0} falló con el error de Windows {1}.", api, result));
    }
}
