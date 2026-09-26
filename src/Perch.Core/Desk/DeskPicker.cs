using Perch.Bluetooth;

namespace Perch.Desk;

/// <summary>Which of the devices on offer to use. Shared by the app and the CLI so they agree.</summary>
public static class DeskPicker
{
    /// <summary>
    /// An explicit request wins (exact id, then a name containing the text). Otherwise the
    /// saved desk, then the first desk-like name, then - only when <paramref name="anyAsLastResort"/> -
    /// whatever is first.
    /// </summary>
    public static BleDevice? Pick(
        IReadOnlyList<BleDevice> devices, string? savedId, string? wanted = null, bool anyAsLastResort = false)
    {
        if (wanted is not null)
            return devices.FirstOrDefault(d => string.Equals(d.Id, wanted, StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault(d => d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));

        return devices.FirstOrDefault(d => d.Id == savedId)
            ?? devices.FirstOrDefault(d => LinakProtocol.LooksLikeADesk(d.Name))
            ?? (anyAsLastResort ? devices.FirstOrDefault() : null);
    }
}
