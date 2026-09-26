using System.IO.Pipes;
using System.Text.Json;

namespace Perch.Ipc;

/// <summary>
/// The desk accepts a single Bluetooth connection, so when the app is running it owns
/// the desk and the CLI asks it to do the work over this named pipe (a Windows named
/// pipe, or a Unix domain socket on macOS - .NET picks). One JSON line in, one JSON line
/// out, then the connection closes.
/// </summary>
public static class ControlProtocol
{
    /// <summary>Per user, so two people signed in to one machine do not collide.</summary>
    public static string PipeName => Bluetooth.SimulatedBluetooth.Requested
        ? $"Perch.Control.{Environment.UserName}.simulator"
        : $"Perch.Control.{Environment.UserName}";

    /// <summary>
    /// Restricts the channel to the user running the app, on both ends and on every OS:
    /// an ACL on Windows, a peer credential check on Unix sockets.
    /// </summary>
    public const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
}

/// <summary>The CLI's exit codes, which the app also reports so the CLI can pass them through.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int NoDesk = 2;
    public const int NoConnect = 3;
    public const int MoveFailed = 4;
}

public sealed record ControlRequest(
    string Command,
    double? HeightCm = null,
    double? DeltaCm = null,
    int? Slot = null,
    int? TimeoutSeconds = null);

public sealed record ControlResponse(
    bool Ok,
    double? HeightCm = null,
    double? TargetCm = null,
    string? Device = null,
    string? Error = null,
    /// <summary>Mirrors the CLI exit codes so the caller can pass the reason straight through.</summary>
    int? Code = null);
