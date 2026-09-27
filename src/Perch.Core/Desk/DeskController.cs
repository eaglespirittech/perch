using System.Diagnostics;
using Perch.Bluetooth;
using static Perch.Desk.LinakProtocol;

namespace Perch.Desk;

/// <summary>
/// Drives an IKEA IDÅSEN over whatever <see cref="IBluetooth"/> the platform provides. The
/// protocol (see <see cref="LinakProtocol"/>) and the move loop live here once, for every
/// operating system.
/// </summary>
public sealed class DeskController : IDisposable
{
    public const double MinCm = LinakProtocol.MinCm;
    public const double MaxCm = LinakProtocol.MaxCm;

    /// <summary>Stop chasing the target once we are this close to it.</summary>
    const double ToleranceCm = 0.15;

    readonly IBluetooth _bluetooth;
    IGattConnection? _connection;
    IGattCharacteristic? _control;
    IGattCharacteristic? _height;
    IGattCharacteristic? _referenceInput;

    public DeskController(IBluetooth bluetooth) => _bluetooth = bluetooth;

    public event Action<double>? HeightChanged;
    public event Action<bool>? ConnectionChanged;

    public double? CurrentCm { get; private set; }
    public double CurrentSpeed { get; private set; }
    public bool IsConnected => _height is not null;
    public string? DeviceName { get; private set; }

    public IBluetooth Bluetooth => _bluetooth;

    public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        Disconnect();

        var connection = await _bluetooth.ConnectAsync(deviceId, ct);
        try
        {
            var control = await connection.GetCharacteristicAsync(ControlService, ControlChar, ct);
            var height = await connection.GetCharacteristicAsync(ReferenceOutputService, HeightChar, ct);
            var referenceInput = await connection.GetCharacteristicAsync(ReferenceInputService, ReferenceInputChar, ct);

            await height.SubscribeAsync(OnHeightNotification, ct);

            _connection = connection;
            _control = control;
            _height = height;
            _referenceInput = referenceInput;
            DeviceName = connection.Name;
            connection.Disconnected += OnDisconnected;
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        await ReadHeightAsync(ct);
        ConnectionChanged?.Invoke(true);
    }

    void OnDisconnected()
    {
        // Leave the objects in place until Disconnect() so a move in flight fails with a
        // write error rather than a null reference.
        ConnectionChanged?.Invoke(false);
    }

    void OnHeightNotification(byte[] value)
    {
        if (!TryDecodeHeight(value, out var cm, out var speed)) return;
        CurrentCm = cm;
        CurrentSpeed = speed;
        HeightChanged?.Invoke(cm);
    }

    public async Task<double> ReadHeightAsync(CancellationToken ct = default)
    {
        if (_height is null) throw new InvalidOperationException("Not connected.");

        var value = await _height.ReadAsync(ct);
        if (!TryDecodeHeight(value, out var cm, out var speed))
            throw new IOException("The desk returned a height value we could not decode.");

        CurrentCm = cm;
        CurrentSpeed = speed;
        HeightChanged?.Invoke(cm);
        return cm;
    }

    /// <summary>
    /// Drives the desk to <paramref name="targetCm"/> and returns once it has settled.
    /// The target has to be re-sent continuously: the controller treats a gap in the
    /// stream as "the operator let go of the button" and halts, which is what keeps
    /// the collision guard meaningful.
    /// </summary>
    public async Task MoveToAsync(double targetCm, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (_control is null || _referenceInput is null) throw new InvalidOperationException("Not connected.");

        targetCm = Math.Clamp(targetCm, MinCm, MaxCm);
        var payload = EncodeTarget(targetCm);

        await _control.WriteAsync(CommandWakeup, ct: ct);
        await _control.WriteAsync(CommandStop, ct: ct);
        await Task.Delay(300, ct);

        var last = await ReadHeightAsync(ct);
        var stalledTicks = 0;
        var nudges = 0;
        var clock = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(60);

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (clock.Elapsed > limit)
                    throw new TimeoutException(
                        $"The desk did not reach the target height within {limit.TotalSeconds:0} seconds.");

                await _referenceInput.WriteAsync(payload, preferWithoutResponse: true, ct);
                await Task.Delay(200, ct);

                var current = await ReadHeightAsync(ct);
                if (Math.Abs(current - targetCm) <= ToleranceCm) break;

                if (Math.Abs(current - last) < 0.05)
                {
                    // The motors take a moment to engage, so only a persistent
                    // stall counts as one.
                    if (++stalledTicks >= 10)
                    {
                        if (++nudges > 3)
                            throw new IOException(
                                "The desk stopped short of the target. Something may be in the way, " +
                                "or it tripped the collision guard - move it manually a little and try again.");
                        await _control.WriteAsync(CommandWakeup, ct: ct);
                        await _control.WriteAsync(CommandStop, ct: ct);
                        stalledTicks = 0;
                    }
                }
                else
                {
                    stalledTicks = 0;
                }

                last = current;
            }
        }
        finally
        {
            // Not the caller's token: a cancelled move must still get its stop through.
            try { await _control.WriteAsync(CommandStop); } catch { /* nothing useful left to do */ }
        }

        await ReadHeightAsync(CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (_control is null) return;
        await _control.WriteAsync(CommandStop);
    }

    public void Disconnect()
    {
        if (_connection is not null)
        {
            _connection.Disconnected -= OnDisconnected;
            _connection.Dispose();
        }

        _connection = null;
        _control = null;
        _height = null;
        _referenceInput = null;
        CurrentCm = null;
    }

    public void Dispose() => Disconnect();
}
