using System.IO.Pipes;
using System.Text.Json;

namespace Perch.Ipc;

/// <summary>
/// Listens for CLI requests while the app is running. One client at a time is plenty:
/// the desk can only do one thing at once anyway.
/// </summary>
public sealed class ControlServer : IDisposable
{
    readonly Func<ControlRequest, Task<ControlResponse>> _handle;
    readonly CancellationTokenSource _stopping = new();

    public ControlServer(Func<ControlRequest, Task<ControlResponse>> handle)
    {
        _handle = handle;
        _ = Task.Run(() => ListenAsync(_stopping.Token));
    }

    async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    ControlProtocol.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    ControlProtocol.Options);

                await pipe.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (line is null) continue;

                ControlResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<ControlRequest>(line, ControlProtocol.Json);
                    response = request is null
                        ? new ControlResponse(false, Error: "Empty request.")
                        : await _handle(request);
                }
                catch (Exception ex)
                {
                    response = new ControlResponse(false, Error: ex.Message);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, ControlProtocol.Json));

                // Unix sockets deliver what was written before close anyway, and do not
                // support waiting for the drain.
                if (OperatingSystem.IsWindows()) pipe.WaitForPipeDrain();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A client that hung up mid-request should not take the listener down.
                await Task.Delay(200, CancellationToken.None);
            }
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
