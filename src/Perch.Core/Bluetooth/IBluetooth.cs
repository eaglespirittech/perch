namespace Perch.Bluetooth;

/// <summary>A Bluetooth LE device as the operating system identifies it.</summary>
/// <param name="Id">
/// Opaque and platform specific: a WinRT device id on Windows, a CoreBluetooth peripheral
/// UUID on macOS. Only ever handed back to the same platform, and saved in settings.
/// </param>
public sealed record BleDevice(string Id, string Name);

/// <summary>
/// The one thing that differs per operating system about talking to the desk: finding
/// it and opening a GATT connection. Everything above this (the Linak protocol, the
/// move loop, stall detection) is shared, in <see cref="Desk.DeskController"/>.
/// </summary>
public interface IBluetooth
{
    /// <summary>
    /// Devices that could be the desk. On Windows these are the paired LE devices; on
    /// macOS, which has no pairing list to read, it is a short scan for anything that
    /// advertises the desk's service or has a desk-like name.
    /// </summary>
    Task<IReadOnlyList<BleDevice>> FindDevicesAsync(CancellationToken ct = default);

    /// <summary>Opens a connection. Throws <see cref="IOException"/> with a message fit for the user.</summary>
    Task<IGattConnection> ConnectAsync(string deviceId, CancellationToken ct = default);

    /// <summary>What to tell someone whose desk does not show up, e.g. where to pair it.</summary>
    string SetupHint { get; }
}

/// <summary>An open connection to one device.</summary>
public interface IGattConnection : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Resolves a characteristic, failing with a user-facing <see cref="IOException"/> if
    /// the device does not expose it (usually because something else holds the desk).
    /// </summary>
    Task<IGattCharacteristic> GetCharacteristicAsync(Guid service, Guid characteristic, CancellationToken ct = default);

    /// <summary>Raised when the link drops for any reason other than <see cref="IDisposable.Dispose"/>.</summary>
    event Action? Disconnected;
}

public interface IGattCharacteristic
{
    Task<byte[]> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes a value. With <paramref name="preferWithoutResponse"/> the platform may skip
    /// the acknowledgement if the characteristic allows it, which matters for the move
    /// loop's steady stream of targets.
    /// </summary>
    Task WriteAsync(byte[] value, bool preferWithoutResponse = false, CancellationToken ct = default);

    /// <summary>Turns on notifications; every value the device pushes goes to <paramref name="onValue"/>.</summary>
    Task SubscribeAsync(Action<byte[]> onValue, CancellationToken ct = default);
}
