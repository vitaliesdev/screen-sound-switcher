using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonitorTv.Core.Tv;

public sealed class TvSettings
{
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    public string ClientKey { get; set; } = "";

    private static string File_ => Path.Combine(AppData.Dir, "tv.json");

    public static TvSettings Load() =>
        File.Exists(File_) ? JsonSerializer.Deserialize<TvSettings>(File.ReadAllText(File_)) ?? new() : new();

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Minimal client for LG webOS TVs (SSAP over wss://ip:3001).</summary>
public sealed class LgTv : IDisposable
{
    private readonly TvSettings _settings;
    private readonly ClientWebSocket _ws = new();
    private int _nextId;

    private LgTv(TvSettings settings) => _settings = settings;

    public static void WakeOnLan(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) throw new InvalidOperationException(Loc.T("La dirección MAC de la tele no está configurada."));
        var parts = mac.Trim().Split('-', ':');
        if (parts.Length != 6 || !parts.All(b => byte.TryParse(b, System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new ArgumentException(Loc.T("Dirección MAC no válida: {0}", mac));
        var macBytes = parts.Select(b => Convert.ToByte(b, 16)).ToArray();

        var packet = Enumerable.Repeat((byte)0xFF, 6).Concat(Enumerable.Repeat(macBytes, 16).SelectMany(b => b)).ToArray();
        using var udp = new UdpClient { EnableBroadcast = true };
        foreach (var port in new[] { 7, 9 })
            udp.Send(packet, packet.Length, "255.255.255.255", port);
    }

    /// <summary>Connects and registers (pairing on first use). The TV asks for confirmation on screen when pairing.</summary>
    public static async Task<LgTv> Connect(TvSettings settings, TimeSpan timeout, bool pairing = false, CancellationToken ct = default)
    {
        if (settings.Ip == "") throw new InvalidOperationException(Loc.T("La IP de la tele no está configurada."));

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var tv = new LgTv(settings);
            // The TV uses a self-signed certificate; we only ever connect to the configured address.
            tv._ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                await tv._ws.ConnectAsync(new Uri($"wss://{settings.Ip}:3001"), cts.Token);
                await tv.Register(pairing ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(10), ct);
                return tv;
            }
            catch (Exception) when (DateTime.UtcNow < deadline && !ct.IsCancellationRequested && !tv.Rejected)
            {
                tv.Dispose();
                await Task.Delay(1000, ct);
            }
            catch
            {
                tv.Dispose();
                throw;
            }
        }
    }

    private bool Rejected;

    private async Task Register(TimeSpan timeout, CancellationToken ct)
    {
        var payload = JsonNode.Parse(Manifest)!.AsObject();
        var register = new JsonObject
        {
            ["type"] = "register",
            ["id"] = "reg",
            ["payload"] = new JsonObject
            {
                ["forcePairing"] = false,
                ["pairingType"] = "PROMPT",
                ["manifest"] = payload,
            },
        };
        if (_settings.ClientKey != "") register["payload"]!["client-key"] = _settings.ClientKey;
        await Send(register, ct);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var msg = await Receive(TimeSpan.FromSeconds(1), ct);
            if (msg == null) continue;
            switch (msg["type"]?.GetValue<string>())
            {
                case "registered":
                    _settings.ClientKey = msg["payload"]!["client-key"]!.GetValue<string>();
                    _settings.Save();
                    return;
                case "error":
                    Rejected = true;
                    throw new InvalidOperationException(Loc.T("La tele rechazó la conexión: {0}", msg["error"]));
            }
        }
        Rejected = true;
        throw new TimeoutException(Loc.T("La tele no aceptó el emparejamiento (acepta el aviso en la pantalla)."));
    }

    public async Task<JsonNode> Request(string uri, JsonObject? payload = null, CancellationToken ct = default)
    {
        var id = $"r{++_nextId}";
        await Send(new JsonObject { ["type"] = "request", ["id"] = id, ["uri"] = uri, ["payload"] = payload ?? new JsonObject() }, ct);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var msg = await Receive(TimeSpan.FromSeconds(1), ct);
            if (msg?["id"]?.GetValue<string>() == id) return msg;
        }
        throw new TimeoutException(Loc.T("La tele no respondió a {0}.", uri));
    }

    /// <summary>Switches the TV to HDMI_1..HDMI_4 or "TV" (live television).</summary>
    public async Task SetInput(string input, CancellationToken ct = default)
    {
        // HDMI_1 -> app com.webos.app.hdmi1 (more reliable on webOS 5 than switchInput)
        var appId = input.Equals("TV", StringComparison.OrdinalIgnoreCase)
            ? "com.webos.app.livetv"
            : "com.webos.app." + input.Replace("_", "").ToLowerInvariant();
        var reply = await Request("ssap://system.launcher/launch", new JsonObject { ["id"] = appId }, ct);
        if (reply["payload"]?["returnValue"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(Loc.T("La tele no pudo abrir '{0}'.", input));
    }

    public Task TurnOff(CancellationToken ct = default) => Request("ssap://system/turnOff", null, ct);

    public Task Toast(string message, CancellationToken ct = default) =>
        Request("ssap://system.notifications/createToast", new JsonObject { ["message"] = message }, ct);

    public async Task<IReadOnlyList<string>> Info(CancellationToken ct = default)
    {
        var lines = new List<string>();
        var fg = await Request("ssap://com.webos.applicationManager/getForegroundAppInfo", null, ct);
        lines.Add($"Now showing: {fg["payload"]?["appId"]}");
        var inputs = await Request("ssap://tv/getExternalInputList", null, ct);
        foreach (var d in inputs["payload"]?["devices"]?.AsArray() ?? [])
            lines.Add($"{d!["id"],-8} {d["label"],-25} {((d["connected"]?.GetValue<bool>() ?? false) ? "connected" : "")}");
        return lines;
    }

    private async Task Send(JsonNode message, CancellationToken ct) =>
        await _ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, ct);

    // Cancelling a pending ReceiveAsync aborts a ClientWebSocket, so a read that outlives a timeout is kept for the next call.
    private readonly byte[] _buffer = new byte[65536];
    private readonly MemoryStream _partial = new();
    private Task<ValueWebSocketReceiveResult>? _pending;

    private async Task<JsonNode?> Receive(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_ws.State == WebSocketState.Open)
        {
            _pending ??= _ws.ReceiveAsync(_buffer.AsMemory(), CancellationToken.None).AsTask();
            var left = deadline - DateTime.UtcNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;

            if (await Task.WhenAny(_pending, Task.Delay(left, ct)) != _pending)
            {
                ct.ThrowIfCancellationRequested();
                return null; // timed out; the read stays pending
            }

            var result = await _pending;
            _pending = null;
            if (result.MessageType == WebSocketMessageType.Close) return null;

            _partial.Write(_buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                var message = JsonNode.Parse(_partial.ToArray());
                _partial.SetLength(0);
                return message;
            }
        }
        return null;
    }

    public void Dispose() => _ws.Dispose();

    // Public LG remote-app manifest used by the webOS pairing flow.
    private const string Manifest = """
        {"manifestVersion":1,"appVersion":"1.1","signed":{"created":"20140509","appId":"com.lge.test","vendorId":"com.lge",
        "localizedAppNames":{"":"LG Remote App","ko-KR":"리모컨 앱","zxx-XX":"ЛГ Rэмotэ AПП"},
        "localizedVendorNames":{"":"LG Electronics"},
        "permissions":["TEST_SECURE","CONTROL_INPUT_TEXT","CONTROL_MOUSE_AND_KEYBOARD","READ_INSTALLED_APPS","READ_LGE_SDX","READ_NOTIFICATIONS","SEARCH","WRITE_SETTINGS","WRITE_NOTIFICATION_ALERT","CONTROL_POWER","READ_CURRENT_CHANNEL","READ_RUNNING_APPS","READ_UPDATE_INFO","UPDATE_FROM_REMOTE_APP","READ_LGE_TV_INPUT_EVENTS","READ_TV_CURRENT_TIME"],
        "serial":"2f930e2d2cfe083771f68e4fe7bb07"},
        "permissions":["LAUNCH","LAUNCH_WEBAPP","APP_TO_APP","CLOSE","TEST_OPEN","TEST_PROTECTED","CONTROL_AUDIO","CONTROL_DISPLAY","CONTROL_INPUT_JOYSTICK","CONTROL_INPUT_MEDIA_RECORDING","CONTROL_INPUT_MEDIA_PLAYBACK","CONTROL_INPUT_TV","CONTROL_POWER","READ_APP_STATUS","READ_CURRENT_CHANNEL","READ_INPUT_DEVICE_LIST","READ_NETWORK_STATE","READ_RUNNING_APPS","READ_TV_CHANNEL_LIST","WRITE_NOTIFICATION_TOAST","READ_POWER_STATE","READ_COUNTRY_INFO"],
        "signatures":[{"signatureVersion":1,"signature":"eyJhbGdvcml0aG0iOiJSU0EtU0hBMjU2Iiwia2V5SWQiOiJ0ZXN0LXNpZ25pbmctY2VydCIsInNpZ25hdHVyZVZlcnNpb24iOjF9.hrVRgjCwXVvE2OOSpDZ58hR+59aFNwYDyjQgKk3auukd7pcegmE2CzPCa0bJ0ZsRAcKkCTJrWo5iDzNhMBWRyaMOv5zWSrthlf7G128qvIlpMT0YNY+n/FaOHE73uLrS/g7swl3/qH/BGFG2Hu4RlL48eb3lLKqTt2xKHdCs6Cd4RMfJPYnzgvI4BNrFUKsjkcu+WD4OO2A27Pq1n50cMchmcaXadJhGrOqH5YmHdOCj5NSHzJYrsW0HPlpuAx/ECMeIZYDh6RMqaFM2DXzdKX9NmmyqzJ3o/0lkk/N97gfVRLW5hA29yeAwaCViZNCP8iC9aO0q9fQojoa7NQnAtw=="}]}
        """;
}
