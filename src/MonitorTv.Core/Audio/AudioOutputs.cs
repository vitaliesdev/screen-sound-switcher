using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MonitorTv.Core.Audio;

/// <summary>Lists playback devices and changes the Windows default output.</summary>
public static class AudioOutputs
{
    public sealed record Output(string Id, string Name, bool IsDefault);

    public static IReadOnlyList<Output> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
        catch (COMException) { /* no default output */ }

        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new Output(d.ID, d.FriendlyName, d.ID == defaultId))
            .ToList();
    }

    public static Output? Current() => List().FirstOrDefault(o => o.IsDefault);

    public static Output? Find(string namePart) =>
        List().FirstOrDefault(o => o.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase));

    /// <summary>Waits until an output matching <paramref name="namePart"/> appears (e.g. a TV that is still waking up).</summary>
    public static async Task<Output?> WaitFor(string namePart, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var found = Find(namePart);
            if (found != null) return found;
            await Task.Delay(500, ct);
        } while (DateTime.UtcNow < deadline);
        return Find(namePart);
    }

    /// <summary>Makes the next connected output of <paramref name="names"/> the default; returns it, or null if none is connected.</summary>
    public static Output? CycleNext(IReadOnlyList<string> names)
    {
        var outputs = List();
        var candidates = names
            .Select(n => outputs.FirstOrDefault(o => o.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            .OfType<Output>()
            .DistinctBy(o => o.Id)
            .ToList();
        if (candidates.Count == 0) return null;

        var current = candidates.FindIndex(o => o.IsDefault);
        var next = candidates[(current + 1) % candidates.Count];
        if (!next.IsDefault) SetDefault(next.Id);
        return next;
    }

    public static void SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        // 0 = console, 1 = multimedia, 2 = communications
        for (var role = 0; role < 3; role++)
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
    }

    // Undocumented but stable COM interface; the same one audio.ps1 already relies on.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility();
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }
}
