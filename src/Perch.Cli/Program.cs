using System.Globalization;
using System.Text.Json;
using Perch.Bluetooth;
using Perch.Desk;
using Perch.Ipc;
using Perch.Platform;

namespace Perch.Cli;

static class Program
{
    const int ExitOk = ExitCodes.Ok;
    const int ExitUsage = ExitCodes.Usage;
    const int ExitNoDesk = ExitCodes.NoDesk;
    const int ExitNoConnect = ExitCodes.NoConnect;
    const int ExitMoveFailed = ExitCodes.MoveFailed;

    static IBluetooth Bluetooth => PlatformServices.Current.Bluetooth;

    static async Task<int> Main(string[] rawArgs)
    {
        Options options;
        try
        {
            options = Options.Parse(rawArgs);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run \"perch-cli help\" for usage.");
            return ExitUsage;
        }

        if (options.WantsHelp || options.Command is null)
        {
            Console.Out.WriteLine(options.Json ? Help.Json() : Help.Text);
            return options.Command is null && !options.WantsHelp ? ExitUsage : ExitOk;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        try
        {
            return options.Command switch
            {
                "help" => Print(options, Help.Text, Help.Json()),
                "version" => Print(options, Version(), Json(new { version = Version() })),
                "list" => await ListAsync(options),
                "status" => await StatusAsync(options),
                "set" => await SetAsync(options, cancel.Token),
                "nudge" => await NudgeAsync(options, cancel.Token),
                "preset" => await PresetAsync(options, cancel.Token),
                "stop" => await StopAsync(options),
                "watch" => await WatchAsync(options, cancel.Token),
                _ => Fail(options, ExitUsage,
                    $"Unknown command \"{options.Command}\". Run \"perch-cli help\" for usage.")
            };
        }
        catch (OperationCanceledException)
        {
            return Fail(options, ExitMoveFailed, "Interrupted.");
        }
    }

    // ---- commands ----------------------------------------------------------

    static async Task<int> ListAsync(Options options)
    {
        IReadOnlyList<BleDevice> devices;
        try
        {
            devices = await FindDevicesAsync(options);
        }
        catch (Exception ex)
        {
            return Fail(options, ExitNoDesk, ex.Message);
        }

        var saved = Settings.Load().DeviceId;

        if (options.Json)
        {
            Console.Out.WriteLine(Json(new
            {
                devices = devices.Select(d => new { name = d.Name, id = d.Id, saved = d.Id == saved })
            }));
            return ExitOk;
        }

        if (devices.Count == 0)
        {
            Console.Error.WriteLine($"No desk found. {Bluetooth.SetupHint}");
            return ExitNoDesk;
        }

        foreach (var device in devices)
            Console.Out.WriteLine($"{(device.Id == saved ? "*" : " ")} {device.Name,-24} {device.Id}");

        if (saved is not null) Console.Out.WriteLine("\n* = the desk saved by the Perch app");
        return ExitOk;
    }

    static async Task<int> StatusAsync(Options options)
    {
        if (await AskTheAppAsync(options, new ControlRequest("status")) is { } viaApp)
            return Render(options, viaApp);

        var (desk, error) = await ConnectAsync(options);
        if (desk is null) return error!.Code;
        using (desk)
        {
            var cm = await desk.ReadHeightAsync();
            return options.Json
                ? Print(options, string.Empty, Json(new
                {
                    ok = true,
                    height_cm = Math.Round(cm, 1),
                    device = desk.DeviceName,
                    moving = Math.Abs(desk.CurrentSpeed) > 0.01,
                    via = "bluetooth"
                }))
                : Print(options, Format(cm), string.Empty);
        }
    }

    static async Task<int> SetAsync(Options options, CancellationToken ct)
    {
        if (options.Argument is null)
            return Fail(options, ExitUsage, "set needs a height in centimetres, e.g. \"perch-cli set 110\".");

        if (!TryParseNumber(options.Argument, out var target))
            return Fail(options, ExitUsage, $"\"{options.Argument}\" is not a number.");

        if (target < DeskController.MinCm || target > DeskController.MaxCm)
            return Fail(options, ExitUsage,
                $"{Format(target)} is outside the desk's range " +
                $"({Format(DeskController.MinCm)} to {Format(DeskController.MaxCm)}).");

        return await MoveAsync(options, target, ct);
    }

    static async Task<int> NudgeAsync(Options options, CancellationToken ct)
    {
        if (options.Argument is null)
            return Fail(options, ExitUsage, "nudge needs a number of centimetres, e.g. \"perch-cli nudge -1.5\".");

        if (!TryParseNumber(options.Argument, out var delta))
            return Fail(options, ExitUsage, $"\"{options.Argument}\" is not a number.");

        if (await AskTheAppAsync(options, new ControlRequest("nudge", DeltaCm: delta,
                TimeoutSeconds: options.TimeoutSeconds)) is { } viaApp)
            return Render(options, viaApp);

        var (desk, error) = await ConnectAsync(options);
        if (desk is null) return error!.Code;
        using (desk)
        {
            var target = Math.Clamp(await desk.ReadHeightAsync() + delta, DeskController.MinCm, DeskController.MaxCm);
            return await RunMoveAsync(options, desk, target, ct);
        }
    }

    static async Task<int> PresetAsync(Options options, CancellationToken ct)
    {
        if (options.Argument is not ("1" or "2"))
            return Fail(options, ExitUsage, "preset needs a slot, either 1 or 2.");

        if (await AskTheAppAsync(options, new ControlRequest("preset", Slot: int.Parse(options.Argument),
                TimeoutSeconds: options.TimeoutSeconds)) is { } viaApp)
            return Render(options, viaApp);

        var settings = Settings.Load();
        var target = options.Argument == "1" ? settings.Preset1 : settings.Preset2;
        return await MoveAsync(options, target, ct);
    }

    static async Task<int> MoveAsync(Options options, double target, CancellationToken ct)
    {
        if (await AskTheAppAsync(options, new ControlRequest("set", HeightCm: target,
                TimeoutSeconds: options.TimeoutSeconds)) is { } viaApp)
            return Render(options, viaApp);

        var (desk, error) = await ConnectAsync(options);
        if (desk is null) return error!.Code;
        using (desk) return await RunMoveAsync(options, desk, target, ct);
    }

    static async Task<int> RunMoveAsync(Options options, DeskController desk, double target, CancellationToken ct)
    {
        if (!options.Quiet && !options.Json)
            Console.Error.WriteLine($"Moving to {Format(target)}...");

        try
        {
            await desk.MoveToAsync(target, ct, TimeSpan.FromSeconds(options.TimeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            return Fail(options, ExitMoveFailed, "Interrupted; the desk was told to stop.");
        }
        catch (Exception ex)
        {
            return Fail(options, ExitMoveFailed, ex.Message);
        }

        var height = desk.CurrentCm ?? target;
        return options.Json
            ? Print(options, string.Empty, Json(new
            {
                ok = true,
                height_cm = Math.Round(height, 1),
                target_cm = Math.Round(target, 1),
                device = desk.DeviceName,
                via = "bluetooth"
            }))
            : Print(options, Format(height), string.Empty);
    }

    static async Task<int> StopAsync(Options options)
    {
        if (await AskTheAppAsync(options, new ControlRequest("stop")) is { } viaApp)
            return Render(options, viaApp);

        var (desk, error) = await ConnectAsync(options);
        if (desk is null) return error!.Code;
        using (desk)
        {
            await desk.StopAsync();
            var cm = await desk.ReadHeightAsync();
            return options.Json
                ? Print(options, string.Empty, Json(new
                {
                    ok = true,
                    stopped = true,
                    height_cm = Math.Round(cm, 1),
                    via = "bluetooth"
                }))
                : Print(options, Format(cm), string.Empty);
        }
    }

    static async Task<int> WatchAsync(Options options, CancellationToken ct)
    {
        var (desk, error) = await ConnectAsync(options);
        if (desk is null) return error!.Code;

        using (desk)
        {
            desk.HeightChanged += cm =>
            {
                if (options.Json)
                    Console.Out.WriteLine(Json(new { height_cm = Math.Round(cm, 1), at = DateTimeOffset.Now }));
                else
                    Console.Out.WriteLine(Format(cm));
            };

            if (!options.Quiet && !options.Json)
                Console.Error.WriteLine("Watching. Press Ctrl+C to stop.");

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C is the documented way out of watch, so it is a clean exit.
            }

            return ExitOk;
        }
    }

    // ---- plumbing ----------------------------------------------------------

    /// <summary>
    /// The app owns the desk's one Bluetooth connection while it is running, so hand the
    /// work to it. Null means it is not running and we should drive the desk ourselves.
    /// </summary>
    static async Task<ControlResponse?> AskTheAppAsync(Options options, ControlRequest request)
    {
        if (options.Direct) return null;
        return await ControlClient.TryAsync(request, options.TimeoutSeconds);
    }

    static int Render(Options options, ControlResponse response)
    {
        if (!response.Ok)
            return Fail(options, response.Code ?? ExitMoveFailed, response.Error ?? "The Perch app reported a failure.");

        if (!options.Json)
            return Print(options, response.HeightCm is { } cm ? Format(cm) : "ok", string.Empty);

        return Print(options, string.Empty, Json(new
        {
            ok = true,
            height_cm = response.HeightCm is { } h ? Math.Round(h, 1) : (double?)null,
            target_cm = response.TargetCm is { } t ? Math.Round(t, 1) : (double?)null,
            device = response.Device,
            via = "app"
        }));
    }

    sealed record Error(int Code, string Message);

    static async Task<(DeskController? Desk, Error? Error)> ConnectAsync(Options options)
    {
        var settings = Settings.Load();
        BleDevice? pick;

        if (options.Device is null && settings.DeviceId is { } savedId)
        {
            // The desk the app last used: connect straight to it rather than listing
            // first, which on macOS means a scan of several seconds.
            pick = new BleDevice(savedId, settings.DeviceName ?? "desk");
        }
        else
        {
            IReadOnlyList<BleDevice> devices;
            try
            {
                devices = await FindDevicesAsync(options);
            }
            catch (Exception ex)
            {
                return (null, Report(options, new Error(ExitNoDesk, ex.Message)));
            }

            pick = DeskPicker.Pick(devices, settings.DeviceId, options.Device);
        }

        if (pick is null)
        {
            var message = options.Device is null
                ? $"No desk found. {Bluetooth.SetupHint} Then run \"perch-cli list\"."
                : $"No device matches \"{options.Device}\". Run \"perch-cli list\" to see them.";
            return (null, Report(options, new Error(ExitNoDesk, message)));
        }

        var desk = new DeskController(Bluetooth);
        try
        {
            await desk.ConnectAsync(pick.Id);
            return (desk, null);
        }
        catch (Exception ex)
        {
            desk.Dispose();
            return (null, Report(options, new Error(ExitNoConnect, ex.Message)));
        }
    }

    static Task<IReadOnlyList<BleDevice>> FindDevicesAsync(Options options)
    {
        // On macOS this is a scan, which is quiet for a few seconds; say so.
        if (!options.Quiet && !options.Json && PlatformServices.Current.Name == "macOS")
            Console.Error.WriteLine("Scanning for the desk...");
        return Bluetooth.FindDevicesAsync();
    }

    static Error Report(Options options, Error error)
    {
        Fail(options, error.Code, error.Message);
        return error;
    }

    static int Fail(Options options, int code, string message)
    {
        if (options.Json)
            Console.Out.WriteLine(Json(new { ok = false, error = message, code }));
        else
            Console.Error.WriteLine(message);

        return code;
    }

    static int Print(Options options, string text, string json)
    {
        var output = options.Json ? json : text;
        if (output.Length > 0) Console.Out.WriteLine(output);
        return ExitOk;
    }

    static string Json(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });

    static string Format(double cm) => $"{cm.ToString("0.0", CultureInfo.InvariantCulture)} cm";

    static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.TrimEnd('c', 'm', 'C', 'M', ' '), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);

    static string Version() =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
