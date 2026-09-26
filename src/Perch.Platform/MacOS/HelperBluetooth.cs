using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Perch.Bluetooth;

namespace Perch.Platform.MacOS;

/// <summary>
/// Bluetooth LE on macOS, through perch-ble: a small Swift program (native/macos/perch-ble)
/// that wraps CoreBluetooth and speaks JSON lines on stdin/stdout. Plain .NET has no
/// CoreBluetooth binding, and a separate process keeps the native part tiny and lets it
/// carry the Info.plist entry macOS insists on before anything touches Bluetooth.
/// </summary>
public sealed class HelperBluetooth : IBluetooth, IDisposable
{
    const string HelperName = "perch-ble";

    readonly object _lock = new();
    HelperProcess? _helper;

    public string SetupHint =>
        "Press a button on the desk to wake it, close the IKEA app on your phone, and check that Perch " +
        "may use Bluetooth in System Settings > Privacy & Security > Bluetooth.";

    public async Task<IReadOnlyList<BleDevice>> FindDevicesAsync(CancellationToken ct = default)
    {
        var reply = await Helper().RequestAsync(new JsonObject { ["op"] = "scan", ["seconds"] = 4 }, TimeSpan.FromSeconds(15), ct);
        return (reply["devices"]?.AsArray() ?? new JsonArray())
            .Select(d => new BleDevice((string)d!["id"]!, (string?)d["name"] ?? "Unknown"))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IGattConnection> ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        var helper = Helper();
        var reply = await helper.RequestAsync(
            new JsonObject { ["op"] = "connect", ["device"] = deviceId, ["timeout"] = 15 }, TimeSpan.FromSeconds(40), ct);
        return new Connection(helper, (string?)reply["name"] ?? "desk");
    }

    /// <summary>The running helper, started on first use and again if it has died.</summary>
    HelperProcess Helper()
    {
        lock (_lock)
        {
            if (_helper is { HasExited: false }) return _helper;
            _helper?.Dispose();
            _helper = new HelperProcess(Locate());
            return _helper;
        }
    }

    /// <summary>
    /// Next to this program (inside Perch.app, or beside perch-cli in its tarball), then an
    /// installed Perch.app. PERCH_BLE_HELPER overrides it for development.
    /// </summary>
    static string Locate()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("PERCH_BLE_HELPER"),
            Path.Combine(AppContext.BaseDirectory, HelperName)
        };

        // A symlink such as /usr/local/bin/perch-cli -> Perch.app/Contents/MacOS/perch-cli.
        if (Environment.ProcessPath is { } exe &&
            File.ResolveLinkTarget(exe, returnFinalTarget: true) is { } target)
            candidates.Add(Path.Combine(Path.GetDirectoryName(target.FullName)!, HelperName));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add($"/Applications/Perch.app/Contents/MacOS/{HelperName}");
        candidates.Add(Path.Combine(home, "Applications", "Perch.app", "Contents", "MacOS", HelperName));

        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            ?? throw new IOException(
                $"Could not find {HelperName}, the part of Perch that talks to Bluetooth. " +
                "Keep it next to perch-cli, or install Perch.app in Applications.");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _helper?.Dispose();
            _helper = null;
        }
    }

    sealed class Connection : IGattConnection
    {
        readonly HelperProcess _helper;
        readonly List<string> _subscribed = new();
        bool _disposed;

        public Connection(HelperProcess helper, string name)
        {
            _helper = helper;
            Name = name;
            _helper.Disconnected += OnDisconnected;
        }

        public string Name { get; }
        public event Action? Disconnected;

        void OnDisconnected() => Disconnected?.Invoke();

        public async Task<IGattCharacteristic> GetCharacteristicAsync(
            Guid service, Guid characteristic, CancellationToken ct = default)
        {
            try
            {
                await _helper.RequestAsync(Target("discover", service, characteristic), TimeSpan.FromSeconds(20), ct);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    "Could not reach the desk's controls. The usual cause is that something else " +
                    "is already connected to it: close the IKEA Desk Control app on your phone, " +
                    $"or another copy of this app, and try again. ({ex.Message})", ex);
            }

            return new Characteristic(this, service, characteristic);
        }

        static JsonObject Target(string op, Guid service, Guid characteristic) => new()
        {
            ["op"] = op,
            ["service"] = service.ToString(),
            ["char"] = characteristic.ToString()
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _helper.Disconnected -= OnDisconnected;
            foreach (var key in _subscribed) _helper.Unsubscribe(key);

            // Best effort: if the helper is gone, so is the connection.
            _helper.Send(new JsonObject { ["op"] = "disconnect" });
        }

        sealed class Characteristic(Connection owner, Guid service, Guid id) : IGattCharacteristic
        {
            static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

            public async Task<byte[]> ReadAsync(CancellationToken ct = default)
            {
                var reply = await owner._helper.RequestAsync(Target("read", service, id), Timeout, ct);
                return Convert.FromHexString((string?)reply["value"] ?? string.Empty);
            }

            public Task WriteAsync(byte[] value, bool preferWithoutResponse = false, CancellationToken ct = default)
            {
                var request = Target("write", service, id);
                request["value"] = Convert.ToHexString(value);
                request["withoutResponse"] = preferWithoutResponse;
                return owner._helper.RequestAsync(request, Timeout, ct);
            }

            public async Task SubscribeAsync(Action<byte[]> onValue, CancellationToken ct = default)
            {
                var key = HelperProcess.Key(id);
                owner._helper.Subscribe(key, onValue);
                owner._subscribed.Add(key);
                await owner._helper.RequestAsync(Target("subscribe", service, id), Timeout, ct);
            }
        }
    }

    /// <summary>One running perch-ble: numbered requests out, replies and events back.</summary>
    sealed class HelperProcess : IDisposable
    {
        readonly Process _process;
        readonly object _writeLock = new();
        readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
        readonly ConcurrentDictionary<string, Action<byte[]>> _notify = new();
        int _nextId;
        string? _lastError;

        public HelperProcess(string path)
        {
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = Process.Start(start) ?? throw new IOException($"Could not start {path}.");
            _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _lastError = e.Data; };
            _process.BeginErrorReadLine();
            _ = Task.Run(ReadLoopAsync);
        }

        public bool HasExited => _process.HasExited;

        public event Action? Disconnected;

        /// <summary>Notifications are keyed by characteristic UUID, normalised.</summary>
        public static string Key(Guid characteristic) => characteristic.ToString("D").ToUpperInvariant();

        public void Subscribe(string key, Action<byte[]> onValue) => _notify[key] = onValue;
        public void Unsubscribe(string key) => _notify.TryRemove(key, out _);

        public async Task<JsonObject> RequestAsync(JsonObject request, TimeSpan timeout, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _nextId);
            request["id"] = id;

            var reply = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = reply;
            try
            {
                if (!Send(request))
                    throw new IOException(ExitMessage());

                var answer = await reply.Task.WaitAsync(timeout, ct);
                if (answer["ok"]?.GetValue<bool>() != true)
                    throw new IOException((string?)answer["error"] ?? "The Bluetooth helper reported a failure.");
                return answer;
            }
            catch (TimeoutException)
            {
                throw new IOException("The desk did not answer over Bluetooth in time.");
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        /// <summary>Fire and forget. False when the helper has gone.</summary>
        public bool Send(JsonObject message)
        {
            try
            {
                lock (_writeLock)
                {
                    if (_process.HasExited) return false;
                    _process.StandardInput.WriteLine(message.ToJsonString());
                    _process.StandardInput.Flush();
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return false;
            }
        }

        async Task ReadLoopAsync()
        {
            try
            {
                while (await _process.StandardOutput.ReadLineAsync() is { } line)
                {
                    JsonObject? message;
                    try
                    {
                        message = JsonNode.Parse(line) as JsonObject;
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (message is null) continue;

                    if (message["id"] is { } idNode)
                    {
                        if (_pending.TryGetValue(idNode.GetValue<int>(), out var waiter))
                            waiter.TrySetResult(message);
                        continue;
                    }

                    switch ((string?)message["event"])
                    {
                        case "notify":
                            var key = ((string?)message["char"] ?? string.Empty).ToUpperInvariant();
                            if (_notify.TryGetValue(key, out var handler))
                                handler(Convert.FromHexString((string?)message["value"] ?? string.Empty));
                            break;

                        case "disconnected":
                            Disconnected?.Invoke();
                            break;
                    }
                }
            }
            catch
            {
                // Fall through: the helper is gone either way.
            }

            var error = new IOException(ExitMessage());
            foreach (var waiter in _pending.Values) waiter.TrySetException(error);
            Disconnected?.Invoke();
        }

        string ExitMessage() =>
            $"The Bluetooth helper stopped{(_lastError is null ? "." : $": {_lastError}")}";

        public void Dispose()
        {
            try
            {
                // Closing stdin is the helper's cue to disconnect and exit.
                lock (_writeLock) _process.StandardInput.Close();
                if (!_process.WaitForExit(2000)) _process.Kill();
            }
            catch
            {
                // Already gone.
            }

            _process.Dispose();
        }
    }
}
