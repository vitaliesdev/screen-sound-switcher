using HidSharp;

namespace MonitorTv.Core.Devices;

/// <summary>
/// Tells whether a SteelSeries Arctis Nova Pro Wireless headset is switched on. The base station stays connected over USB, so
/// Windows never reports the headset appearing or disappearing; the base answers a status request instead.
/// Protocol facts (vendor 0x1038, product 0x12E0, interface 4, request 06 B0, status in byte 15) are from the open-source
/// HeadsetControl project's documentation of this device.
/// </summary>
public static class ArctisHeadset
{
    private const int VendorId = 0x1038;
    private static readonly int[] ProductIds = [0x12E0, 0x12E5]; // Nova Pro Wireless, Nova Pro Wireless X
    private const int StatusByte = 15;
    private const byte Offline = 0x01;

    public enum State { NoBase, Off, On, Charging }

    public sealed record Reading(State State, int? BatteryPercent, string Detail);

    /// <summary>True when a supported base station is plugged in.</summary>
    public static bool BasePresent => Candidates().Any();

    /// <summary>A reader that keeps the HID stream open between polls, which is much steadier than opening it for every question.</summary>
    public sealed class Monitor : IDisposable
    {
        private HidStream? _stream;
        private HidDevice? _device;

        public Reading Read()
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (_stream == null && !Open())
                        return new Reading(State.NoBase, null, "No se encontró o no se pudo abrir la base de los Arctis.");
                    return Ask();
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
                {
                    Close(); // reopen on the next attempt
                    if (attempt == 2) return new Reading(State.NoBase, null, $"Error leyendo la base: {ex.Message}");
                    Thread.Sleep(200);
                }
            }
            return new Reading(State.NoBase, null, "Error leyendo la base.");
        }

        /// <summary>Interface 4 exposes two HID collections; only one answers the status request, so each is tried in turn.</summary>
        private bool Open()
        {
            foreach (var candidate in Candidates())
            {
                if (!candidate.TryOpen(out var stream)) continue;
                stream.ReadTimeout = 1000;
                stream.WriteTimeout = 1000;
                _device = candidate;
                _stream = stream;
                try
                {
                    Ask();
                    return true;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
                {
                    Close();
                }
            }
            return false;
        }

        private Reading Ask()
        {
            var request = new byte[Math.Max(_device!.GetMaxOutputReportLength(), 31)];
            request[0] = 0x06;
            request[1] = 0xB0;
            _stream!.Write(request);

            // the base also sends unsolicited reports (volume, mic mute...); skip them until the status reply shows up
            byte[] response = [];
            for (var i = 0; i < 6; i++)
            {
                response = _stream.Read();
                if (response.Length > 1 && response[0] == 0x06 && response[1] == 0xB0) break;
            }
            if (response.Length <= StatusByte) throw new IOException($"respuesta demasiado corta ({response.Length} bytes)");

            var status = response[StatusByte];
            int? battery = Math.Clamp(response[6] * 100 / 8, 0, 100);
            var detail = $"bytes={Convert.ToHexString(response.AsSpan(0, Math.Min(18, response.Length)))}";
            return status == Offline ? new Reading(State.Off, null, detail)
                : status == 0x02 ? new Reading(State.Charging, battery, detail)
                : new Reading(State.On, battery, detail);
        }

        private void Close()
        {
            _stream?.Dispose();
            _stream = null;
        }

        public void Dispose() => Close();
    }

    public static Reading Read()
    {
        using var monitor = new Monitor();
        return monitor.Read();
    }

    /// <summary>The HID collections of the base's fourth interface (index 4 in the device path), in a stable order.</summary>
    private static IEnumerable<HidDevice> Candidates() =>
        DeviceList.Local.GetHidDevices(VendorId)
            .Where(d => ProductIds.Contains(d.ProductID) && d.DevicePath.Contains("mi_04", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase);
}
