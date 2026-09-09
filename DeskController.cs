using System.Diagnostics;
using System.IO;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Perch;

/// <summary>
/// Talks to an IKEA Idasen (Linak DPG1C) desk over Bluetooth LE.
///
/// The desk exposes three vendor services:
///   control       99fa0001 / char 99fa0002 -- wake up, stop, manual up/down
///   reference out 99fa0020 / char 99fa0021 -- notifies height + speed
///   reference in  99fa0030 / char 99fa0031 -- target height; the desk drives
///                                             itself there as long as the
///                                             target keeps being re-written
///
/// Height is a little endian uint16 in 0.1 mm above the 620 mm bottom stop,
/// speed a little endian int16 in the same units per second.
/// </summary>
public sealed class DeskController : IDisposable
{
    static readonly Guid ControlService = new("99fa0001-338a-1024-8a49-009c0215f78a");
    static readonly Guid ControlChar = new("99fa0002-338a-1024-8a49-009c0215f78a");
    static readonly Guid ReferenceOutputService = new("99fa0020-338a-1024-8a49-009c0215f78a");
    static readonly Guid HeightChar = new("99fa0021-338a-1024-8a49-009c0215f78a");
    static readonly Guid ReferenceInputService = new("99fa0030-338a-1024-8a49-009c0215f78a");
    static readonly Guid ReferenceInputChar = new("99fa0031-338a-1024-8a49-009c0215f78a");

    static readonly byte[] CommandWakeup = { 0xFE, 0x00 };
    static readonly byte[] CommandStop = { 0xFF, 0x00 };

    public const double MinCm = 62.0;
    public const double MaxCm = 127.0;

    /// <summary>
    /// The controller counts in 0.1 mm, so a centimetre is 100 counts. Getting this
    /// wrong is silent and dangerous: the desk happily drives to a bogus target.
    /// </summary>
    const double CountsPerCm = 100.0;

    /// <summary>Stop chasing the target once we are this close to it.</summary>
    const double ToleranceCm = 0.15;

    BluetoothLEDevice? _device;
    readonly List<GattDeviceService> _services = new();
    GattCharacteristic? _control;
    GattCharacteristic? _height;
    GattCharacteristic? _referenceInput;

    public event Action<double>? HeightChanged;
    public event Action<bool>? ConnectionChanged;

    public double? CurrentCm { get; private set; }
    public double CurrentSpeed { get; private set; }
    public bool IsConnected => _height is not null;
    public string? DeviceName { get; private set; }

    /// <summary>Every Bluetooth LE device that has been paired in Windows settings.</summary>
    public static async Task<IReadOnlyList<DeviceInformation>> ListPairedDevicesAsync()
    {
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var found = await DeviceInformation.FindAllAsync(selector);
        return found.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task ConnectAsync(string deviceId)
    {
        Disconnect();

        _device = await BluetoothLEDevice.FromIdAsync(deviceId)
            ?? throw new IOException("Windows could not open that Bluetooth device. Is it still paired?");
        DeviceName = _device.Name;

        _control = await GetCharacteristicAsync(ControlService, ControlChar);
        _height = await GetCharacteristicAsync(ReferenceOutputService, HeightChar);
        _referenceInput = await GetCharacteristicAsync(ReferenceInputService, ReferenceInputChar);

        _height.ValueChanged += OnHeightNotification;
        var status = await _height.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (status != GattCommunicationStatus.Success)
            throw new IOException($"The desk refused height notifications ({status}).");

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        await ReadHeightAsync();
        ConnectionChanged?.Invoke(true);
    }

    async Task<GattCharacteristic> GetCharacteristicAsync(Guid service, Guid characteristic)
    {
        // Discovery is flaky while the desk is waking up, so give it a couple of tries
        // before deciding something is really wrong.
        for (var attempt = 1; ; attempt++)
        {
            var services = await _device!.GetGattServicesForUuidAsync(service, BluetoothCacheMode.Uncached);
            if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
            {
                var svc = services.Services[0];
                _services.Add(svc);

                var chars = await svc.GetCharacteristicsForUuidAsync(characteristic, BluetoothCacheMode.Uncached);
                if (chars.Status == GattCommunicationStatus.Success && chars.Characteristics.Count > 0)
                    return chars.Characteristics[0];
            }

            if (attempt == 3)
                throw new IOException(
                    "Could not reach the desk's controls. The usual cause is that something else " +
                    "is already connected to it: close the IKEA Desk Control app on your phone, " +
                    "or another copy of this app, and try again. " +
                    $"(service {service}, status {services.Status})");

            await Task.Delay(400);
        }
    }

    void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            ConnectionChanged?.Invoke(false);
    }

    void OnHeightNotification(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (!TryParse(args.CharacteristicValue, out var cm, out var speed)) return;
        CurrentCm = cm;
        CurrentSpeed = speed;
        HeightChanged?.Invoke(cm);
    }

    public async Task<double> ReadHeightAsync()
    {
        if (_height is null) throw new InvalidOperationException("Not connected.");

        var result = await _height.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success)
            throw new IOException($"Could not read the desk height ({result.Status}).");
        if (!TryParse(result.Value, out var cm, out var speed))
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
    public async Task MoveToAsync(double targetCm, CancellationToken ct)
    {
        if (_control is null || _referenceInput is null) throw new InvalidOperationException("Not connected.");

        targetCm = Math.Clamp(targetCm, MinCm, MaxCm);
        var raw = (ushort)Math.Round((targetCm - MinCm) * CountsPerCm);
        byte[] payload = { (byte)(raw & 0xFF), (byte)(raw >> 8) };

        await WriteAsync(_control, CommandWakeup);
        await WriteAsync(_control, CommandStop);
        await Task.Delay(300, ct);

        var last = await ReadHeightAsync();
        var stalledTicks = 0;
        var nudges = 0;
        var clock = Stopwatch.StartNew();

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (clock.Elapsed > TimeSpan.FromSeconds(60))
                    throw new TimeoutException("The desk did not reach the target height within 60 seconds.");

                await WriteAsync(_referenceInput, payload, preferWithoutResponse: true);
                await Task.Delay(200, ct);

                var current = await ReadHeightAsync();
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
                        await WriteAsync(_control, CommandWakeup);
                        await WriteAsync(_control, CommandStop);
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
            try { await WriteAsync(_control, CommandStop); } catch { /* nothing useful left to do */ }
        }

        await ReadHeightAsync();
    }

    public async Task StopAsync()
    {
        if (_control is null) return;
        await WriteAsync(_control, CommandStop);
    }

    static async Task WriteAsync(GattCharacteristic characteristic, byte[] data, bool preferWithoutResponse = false)
    {
        var writer = new DataWriter();
        writer.WriteBytes(data);

        var option = preferWithoutResponse &&
                     characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

        var result = await characteristic.WriteValueWithResultAsync(writer.DetachBuffer(), option);
        if (result.Status != GattCommunicationStatus.Success)
            throw new IOException($"Write to the desk failed ({result.Status}).");
    }

    static bool TryParse(IBuffer buffer, out double cm, out double speed)
    {
        cm = 0;
        speed = 0;
        if (buffer.Length < 4) return false;

        var bytes = new byte[buffer.Length];
        DataReader.FromBuffer(buffer).ReadBytes(bytes);

        var rawHeight = (ushort)(bytes[0] | (bytes[1] << 8));
        var rawSpeed = (short)(bytes[2] | (bytes[3] << 8));

        cm = MinCm + rawHeight / CountsPerCm;
        speed = rawSpeed / CountsPerCm;
        return true;
    }

    public void Disconnect()
    {
        if (_height is not null) _height.ValueChanged -= OnHeightNotification;
        if (_device is not null) _device.ConnectionStatusChanged -= OnConnectionStatusChanged;

        _control = null;
        _height = null;
        _referenceInput = null;

        foreach (var service in _services) service.Dispose();
        _services.Clear();

        _device?.Dispose();
        _device = null;
        CurrentCm = null;
    }

    public void Dispose() => Disconnect();
}
