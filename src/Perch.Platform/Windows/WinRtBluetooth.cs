using System.Runtime.InteropServices.WindowsRuntime;
using Perch.Bluetooth;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using ValueChangedHandler = Windows.Foundation.TypedEventHandler<
    Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic,
    Windows.Devices.Bluetooth.GenericAttributeProfile.GattValueChangedEventArgs>;

namespace Perch.Platform.Windows;

/// <summary>Bluetooth LE through the Windows Runtime APIs. The desk has to be paired in Windows settings first.</summary>
public sealed class WinRtBluetooth : IBluetooth
{
    public string SetupHint =>
        "Pair it in Settings > Bluetooth & devices > Add device, holding the pairing button on the desk's control box.";

    /// <summary>Every Bluetooth LE device that has been paired in Windows settings.</summary>
    public async Task<IReadOnlyList<BleDevice>> FindDevicesAsync(CancellationToken ct = default)
    {
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var found = await DeviceInformation.FindAllAsync(selector).AsTask(ct);
        return found
            .Select(d => new BleDevice(d.Id, d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IGattConnection> ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        var device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(ct)
            ?? throw new IOException("Windows could not open that Bluetooth device. Is it still paired?");
        return new Connection(device);
    }

    sealed class Connection : IGattConnection
    {
        readonly BluetoothLEDevice _device;
        readonly List<GattDeviceService> _services = new();
        readonly List<(GattCharacteristic Characteristic, ValueChangedHandler Handler)> _subscriptions = new();

        public Connection(BluetoothLEDevice device)
        {
            _device = device;
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        }

        public string Name => _device.Name;
        public event Action? Disconnected;

        void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                Disconnected?.Invoke();
        }

        public async Task<IGattCharacteristic> GetCharacteristicAsync(
            Guid service, Guid characteristic, CancellationToken ct = default)
        {
            // Discovery is flaky while the desk is waking up, so give it a couple of tries
            // before deciding something is really wrong.
            for (var attempt = 1; ; attempt++)
            {
                var services = await _device.GetGattServicesForUuidAsync(service, BluetoothCacheMode.Uncached).AsTask(ct);
                if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
                {
                    var svc = services.Services[0];
                    _services.Add(svc);

                    var chars = await svc.GetCharacteristicsForUuidAsync(characteristic, BluetoothCacheMode.Uncached).AsTask(ct);
                    if (chars.Status == GattCommunicationStatus.Success && chars.Characteristics.Count > 0)
                        return new Characteristic(this, chars.Characteristics[0]);
                }

                if (attempt == 3)
                    throw new IOException(
                        "Could not reach the desk's controls. The usual cause is that something else " +
                        "is already connected to it: close the IKEA Desk Control app on your phone, " +
                        "or another copy of this app, and try again. " +
                        $"(service {service}, status {services.Status})");

                await Task.Delay(400, ct);
            }
        }

        public void Dispose()
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            foreach (var (characteristic, handler) in _subscriptions)
                characteristic.ValueChanged -= handler;
            _subscriptions.Clear();

            foreach (var service in _services) service.Dispose();
            _services.Clear();
            _device.Dispose();
        }

        sealed class Characteristic(Connection owner, GattCharacteristic inner) : IGattCharacteristic
        {
            public async Task<byte[]> ReadAsync(CancellationToken ct = default)
            {
                var result = await inner.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct);
                if (result.Status != GattCommunicationStatus.Success)
                    throw new IOException($"Could not read from the desk ({result.Status}).");
                return result.Value.ToArray();
            }

            public async Task WriteAsync(byte[] value, bool preferWithoutResponse = false, CancellationToken ct = default)
            {
                var option = preferWithoutResponse &&
                             inner.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse
                    : GattWriteOption.WriteWithResponse;

                var result = await inner.WriteValueWithResultAsync(value.AsBuffer(), option).AsTask(ct);
                if (result.Status != GattCommunicationStatus.Success)
                    throw new IOException($"Write to the desk failed ({result.Status}).");
            }

            public async Task SubscribeAsync(Action<byte[]> onValue, CancellationToken ct = default)
            {
                // Kept so Dispose can take off exactly the delegate that went on.
                ValueChangedHandler handler = (_, args) => onValue(args.CharacteristicValue.ToArray());
                inner.ValueChanged += handler;
                owner._subscriptions.Add((inner, handler));

                var status = await inner.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct);
                if (status != GattCommunicationStatus.Success)
                    throw new IOException($"The desk refused height notifications ({status}).");
            }
        }
    }
}
