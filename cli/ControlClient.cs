using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Perch.Ipc;

namespace Perch.Cli;

/// <summary>
/// Talks to a running Perch app. Returns null when the app is not running, which is the
/// signal to fall back to driving the desk over Bluetooth directly.
/// </summary>
static class ControlClient
{
    public static async Task<ControlResponse?> TryAsync(ControlRequest request, int timeoutSeconds)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ControlProtocol.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);

            // The app is either there or it is not; no reason to wait long to find out.
            await pipe.ConnectAsync(400);

            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, ControlProtocol.Json));

            // A move can legitimately take a while, so this wait tracks the caller's timeout.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 15));
            var line = await reader.ReadLineAsync(deadline.Token);

            return line is null
                ? new ControlResponse(false, Error: "The Perch app closed the connection without answering.")
                : JsonSerializer.Deserialize<ControlResponse>(line, ControlProtocol.Json);
        }
        catch (TimeoutException)
        {
            return null; // app not running
        }
        catch (IOException)
        {
            return null; // pipe went away between connect and write
        }
        catch (OperationCanceledException)
        {
            return new ControlResponse(false, Error: "The Perch app did not answer in time.");
        }
    }
}
