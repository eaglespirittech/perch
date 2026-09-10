using System.Text.Json;

namespace Perch.Ipc;

/// <summary>
/// The desk accepts a single Bluetooth connection, so when the app is running it owns
/// the desk and the CLI asks it to do the work over this named pipe. One JSON line in,
/// one JSON line out, then the connection closes.
/// </summary>
public static class ControlProtocol
{
    /// <summary>Per user, so two people signed in to one machine do not collide.</summary>
    public static string PipeName => $"Perch.Control.{Environment.UserName}";

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
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
