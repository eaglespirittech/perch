using Perch.Desk;

namespace Perch.Bluetooth;

/// <summary>
/// A pretend IDÅSEN that speaks the real Linak protocol, for working on the app or the
/// CLI without a desk (set PERCH_SIMULATOR=1), and for the tests. It behaves like the
/// hardware in the ways the move loop depends on: it only moves while targets keep
/// arriving, travels at a realistic speed, and pushes height notifications.
/// </summary>
public sealed class SimulatedBluetooth : IBluetooth
{
    public const string DeviceId = "simulated-desk";

    /// <summary>
    /// PERCH_SIMULATOR=1. Also moves settings and the app/CLI channel aside, so a simulated
    /// session never touches the real desk's settings or a real Perch that is running.
    /// </summary>
    public static bool Requested { get; } =
        Environment.GetEnvironmentVariable("PERCH_SIMULATOR") is "1" or "true";

    /// <summary>Roughly what the real desk manages, in cm per second.</summary>
    public double SpeedCmPerSecond { get; init; } = 3.8;

    /// <summary>Stops the desk here as if it hit something, to exercise stall handling.</summary>
    public double? Obstacle { get; set; }

    public double HeightCm { get; private set; } = 72.0;

    public IReadOnlyList<string> Log => _log;
    readonly List<string> _log = new();

    public string SetupHint => "The simulator is always available.";

    public Task<IReadOnlyList<BleDevice>> FindDevicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BleDevice>>(new[] { new BleDevice(DeviceId, "Simulated desk") });

    public Task<IGattConnection> ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        if (deviceId != DeviceId) throw new IOException($"No simulated device \"{deviceId}\".");
        return Task.FromResult<IGattConnection>(new Connection(this));
    }

    sealed class Connection : IGattConnection
    {
        readonly SimulatedBluetooth _desk;
        readonly Timer _motor;
        readonly object _lock = new();
        Action<byte[]>? _notify;
        double? _target;
        DateTime _lastTarget;
        DateTime _lastTick = DateTime.UtcNow;
        bool _disposed;

        public Connection(SimulatedBluetooth desk)
        {
            _desk = desk;
            _motor = new Timer(_ => Tick(), null, 50, 50);
        }

        public string Name => "Simulated desk";
        public event Action? Disconnected { add { } remove { } }

        public Task<IGattCharacteristic> GetCharacteristicAsync(Guid service, Guid characteristic, CancellationToken ct = default) =>
            Task.FromResult<IGattCharacteristic>(new Characteristic(this, characteristic));

        void Tick()
        {
            double height;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastTick).TotalSeconds;
                _lastTick = now;

                // A pause in the stream of targets is the operator letting go.
                if (_target is not { } target || now - _lastTarget > TimeSpan.FromMilliseconds(500))
                {
                    _target = null;
                    return;
                }

                var step = Math.Min(Math.Abs(target - _desk.HeightCm), _desk.SpeedCmPerSecond * elapsed);
                var next = _desk.HeightCm + Math.Sign(target - _desk.HeightCm) * step;
                if (_desk.Obstacle is { } wall &&
                    ((_desk.HeightCm <= wall && next > wall) || (_desk.HeightCm >= wall && next < wall)))
                    next = wall;
                if (Math.Abs(next - _desk.HeightCm) < 1e-9) return;

                _desk.HeightCm = next;
                height = next;
            }

            _notify?.Invoke(LinakProtocol.EncodeHeight(height, 0));
        }

        void Write(Guid characteristic, byte[] value)
        {
            lock (_lock)
            {
                if (characteristic == LinakProtocol.ControlChar)
                {
                    _desk._log.Add(value.SequenceEqual(LinakProtocol.CommandStop) ? "stop" : "wake");
                    if (value.SequenceEqual(LinakProtocol.CommandStop)) _target = null;
                }
                else if (characteristic == LinakProtocol.ReferenceInputChar)
                {
                    var raw = value[0] | (value[1] << 8);
                    _target = LinakProtocol.MinCm + raw / LinakProtocol.CountsPerCm;
                    _lastTarget = DateTime.UtcNow;
                }
            }
        }

        byte[] Read()
        {
            lock (_lock) return LinakProtocol.EncodeHeight(_desk.HeightCm, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _motor.Dispose();
        }

        sealed class Characteristic(Connection owner, Guid id) : IGattCharacteristic
        {
            public Task<byte[]> ReadAsync(CancellationToken ct = default) => Task.FromResult(owner.Read());

            public Task WriteAsync(byte[] value, bool preferWithoutResponse = false, CancellationToken ct = default)
            {
                owner.Write(id, value);
                return Task.CompletedTask;
            }

            public Task SubscribeAsync(Action<byte[]> onValue, CancellationToken ct = default)
            {
                owner._notify += onValue;
                return Task.CompletedTask;
            }
        }
    }
}
