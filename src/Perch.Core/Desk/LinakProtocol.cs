namespace Perch.Desk;

/// <summary>
/// The IKEA IDÅSEN is a rebranded Linak DPG1C controller. It exposes three vendor services:
///   control       99fa0001 / char 99fa0002 -- wake up, stop, manual up/down
///   reference out 99fa0020 / char 99fa0021 -- notifies height + speed
///   reference in  99fa0030 / char 99fa0031 -- target height; the desk drives
///                                             itself there as long as the
///                                             target keeps being re-written
///
/// Height is a little endian uint16 in 0.1 mm above the 620 mm bottom stop, speed a
/// little endian int16 in the same units per second.
/// </summary>
public static class LinakProtocol
{
    public static readonly Guid ControlService = new("99fa0001-338a-1024-8a49-009c0215f78a");
    public static readonly Guid ControlChar = new("99fa0002-338a-1024-8a49-009c0215f78a");
    public static readonly Guid ReferenceOutputService = new("99fa0020-338a-1024-8a49-009c0215f78a");
    public static readonly Guid HeightChar = new("99fa0021-338a-1024-8a49-009c0215f78a");
    public static readonly Guid ReferenceInputService = new("99fa0030-338a-1024-8a49-009c0215f78a");
    public static readonly Guid ReferenceInputChar = new("99fa0031-338a-1024-8a49-009c0215f78a");

    public static readonly byte[] CommandWakeup = { 0xFE, 0x00 };
    public static readonly byte[] CommandStop = { 0xFF, 0x00 };

    public const double MinCm = 62.0;
    public const double MaxCm = 127.0;

    /// <summary>
    /// The controller counts in 0.1 mm, so a centimetre is 100 counts. Getting this
    /// wrong is silent and dangerous: the desk happily drives to a bogus target.
    /// </summary>
    public const double CountsPerCm = 100.0;

    public static byte[] EncodeTarget(double cm)
    {
        var raw = (ushort)Math.Round((Math.Clamp(cm, MinCm, MaxCm) - MinCm) * CountsPerCm);
        return new[] { (byte)(raw & 0xFF), (byte)(raw >> 8) };
    }

    public static bool TryDecodeHeight(ReadOnlySpan<byte> value, out double cm, out double speed)
    {
        cm = 0;
        speed = 0;
        if (value.Length < 4) return false;

        var rawHeight = (ushort)(value[0] | (value[1] << 8));
        var rawSpeed = (short)(value[2] | (value[3] << 8));

        cm = MinCm + rawHeight / CountsPerCm;
        speed = rawSpeed / CountsPerCm;
        return true;
    }

    public static byte[] EncodeHeight(double cm, double speed)
    {
        var raw = (ushort)Math.Round((Math.Clamp(cm, MinCm, MaxCm) - MinCm) * CountsPerCm);
        var rawSpeed = (short)Math.Round(speed * CountsPerCm);
        return new[] { (byte)(raw & 0xFF), (byte)(raw >> 8), (byte)(rawSpeed & 0xFF), (byte)((ushort)rawSpeed >> 8) };
    }

    /// <summary>Names a desk is likely to advertise under, used to pick one when none is saved.</summary>
    public static bool LooksLikeADesk(string name) =>
        name.Contains("desk", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("lift", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("linak", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("idasen", StringComparison.OrdinalIgnoreCase);
}
