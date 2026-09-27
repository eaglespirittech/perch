using System.Globalization;
using Perch.Bluetooth;
using Perch.Ipc;
using Perch.Scheduling;

namespace Perch.Desk;

public enum ConnectionState
{
    Offline,
    Connecting,
    Online
}

/// <summary>
/// What the app does, without any of what it looks like: finding and connecting to the
/// desk, one move at a time, presets, the schedule, and answering perch-cli. The window
/// (any window, on any OS) binds to this and forwards clicks to it.
///
/// Construct it on the UI thread. It captures that thread's synchronisation context and
/// does all of its work there - scheduled moves and CLI requests included - so its state
/// is only ever touched from one thread, and every event arrives on the UI thread.
/// </summary>
public sealed class DeskSession : IDisposable
{
    readonly SynchronizationContext? _context = SynchronizationContext.Current;
    readonly List<BleDevice> _devices = new();

    /// <summary>One move at a time; a scheduled move queues behind a manual one.</summary>
    readonly SemaphoreSlim _gate = new(1, 1);
    CancellationTokenSource? _move;

    public DeskSession(IBluetooth bluetooth, Settings settings, Scheduler? scheduler = null)
    {
        Settings = settings;
        Desk = new DeskController(bluetooth);
        Scheduler = scheduler ?? new Scheduler();

        Desk.HeightChanged += cm => Post(() => HeightChanged?.Invoke(cm));
        Desk.ConnectionChanged += connected => Post(() =>
        {
            if (!connected) SetOffline("The desk dropped the Bluetooth connection.");
        });

        Scheduler.Schedule = settings.Schedule;
        Scheduler.Enabled = settings.ScheduleEnabled;
        Scheduler.MoveRequested += (cm, reason) => Post(async () => await RunScheduledMoveAsync(cm, reason));
        Scheduler.Ticked += () => Post(() => ScheduleChanged?.Invoke());
    }

    public Settings Settings { get; }
    public DeskController Desk { get; }
    public Scheduler Scheduler { get; }
    public IBluetooth Bluetooth => Desk.Bluetooth;

    public IReadOnlyList<BleDevice> Devices => _devices;
    public BleDevice? Selected { get; private set; }

    public ConnectionState Connection { get; private set; }
    public string ConnectionText { get; private set; } = "Not connected";
    public string Status { get; private set; } = "Ready.";
    public bool IsBusy { get; private set; }

    /// <summary>Where a move in progress is heading.</summary>
    public double? MoveTarget { get; private set; }

    public event Action<string>? StatusChanged;
    public event Action? ConnectionChanged;
    public event Action<double>? HeightChanged;
    public event Action? DevicesChanged;
    public event Action? BusyChanged;
    public event Action? PresetsChanged;
    public event Action? ScheduleChanged;

    /// <summary>A move is about to go to this height, so the "Move to" field should show it.</summary>
    public event Action<double>? TargetShown;

    // ---- devices -----------------------------------------------------------

    /// <summary>Called once at start-up: reconnect to the saved desk, then fill the device list.</summary>
    public async Task StartAsync()
    {
        if (Settings.DeviceId is { } id)
        {
            // Connect before listing: on macOS listing means a scan, which takes seconds.
            Selected = new BleDevice(id, Settings.DeviceName ?? "desk");
            await ConnectAsync();
        }

        await ScanAsync(autoConnect: false);
    }

    public async Task ScanAsync(bool autoConnect)
    {
        SetStatus("Looking for the desk...");
        try
        {
            var found = await Bluetooth.FindDevicesAsync();
            _devices.Clear();
            _devices.AddRange(found);

            // Keep the connected desk in the list even if a scan did not hear from it,
            // which on macOS is normal once something is connected.
            if (Selected is { } current && Desk.IsConnected && _devices.All(d => d.Id != current.Id))
                _devices.Insert(0, current);

            DevicesChanged?.Invoke();

            if (_devices.Count == 0)
            {
                SetStatus($"No desk found. {Bluetooth.SetupHint}");
                return;
            }

            if (!Desk.IsConnected)
                Selected = DeskPicker.Pick(_devices, Settings.DeviceId, anyAsLastResort: true);

            SetStatus(Desk.IsConnected ? Status : $"Found {_devices.Count} device(s).");

            if (autoConnect && !Desk.IsConnected && Selected is { } pick && pick.Id == Settings.DeviceId)
                await ConnectAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    public async Task SelectDeviceAsync(BleDevice device)
    {
        if (Desk.IsConnected) Desk.Disconnect();
        Selected = device;
        await ConnectAsync();
    }

    public async Task ToggleConnectionAsync()
    {
        if (Desk.IsConnected)
        {
            Desk.Disconnect();
            SetOffline("Disconnected.");
            return;
        }

        await ConnectAsync();
    }

    async Task ConnectAsync()
    {
        if (Selected is not { } item)
        {
            SetStatus("Pick a desk from the menu first.");
            return;
        }

        SetStatus($"Connecting to {item.Name}...");
        SetConnection(ConnectionState.Connecting, $"Connecting to {item.Name}");
        try
        {
            await Desk.ConnectAsync(item.Id);

            // The platform may know a better name than the one saved.
            var name = Desk.DeviceName ?? item.Name;
            Selected = item with { Name = name };
            Settings.DeviceId = item.Id;
            Settings.DeviceName = name;
            Settings.Save();

            SetConnection(ConnectionState.Online, $"Connected to {name}");
            SetStatus("Connected.");
        }
        catch (Exception ex)
        {
            Desk.Disconnect();
            SetOffline(ex.Message);
        }
    }

    /// <summary>Reconnects to the saved desk, which a scheduled move needs after a sleep or a drop.</summary>
    async Task<bool> EnsureConnectedAsync()
    {
        if (Desk.IsConnected) return true;
        if (Settings.DeviceId is null) return false;

        Selected ??= new BleDevice(Settings.DeviceId, Settings.DeviceName ?? "desk");
        if (Selected.Id != Settings.DeviceId)
            Selected = new BleDevice(Settings.DeviceId, Settings.DeviceName ?? "desk");

        await ConnectAsync();
        return Desk.IsConnected;
    }

    // ---- movement ----------------------------------------------------------

    public async Task MoveToAsync(double targetCm)
    {
        if (!Desk.IsConnected)
        {
            SetStatus("Connect to the desk first, from the menu at the top right.");
            return;
        }

        targetCm = Clamp(targetCm);
        TargetShown?.Invoke(targetCm);
        await RunMoveAsync(targetCm, $"Moving to {Cm(targetCm)}...");
    }

    public async Task NudgeAsync(double deltaCm)
    {
        if (Desk.CurrentCm is not { } current)
        {
            SetStatus("Connect to the desk first.");
            return;
        }

        await MoveToAsync(current + deltaCm);
    }

    public async Task StopAsync()
    {
        _move?.Cancel();
        try
        {
            await Desk.StopAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    async Task RunScheduledMoveAsync(double targetCm, string reason)
    {
        if (!await EnsureConnectedAsync())
        {
            SetStatus($"Skipped the {reason}: no connection to the desk.");
            return;
        }

        TargetShown?.Invoke(targetCm);
        await RunMoveAsync(targetCm, $"{char.ToUpperInvariant(reason[0])}{reason[1..]}: moving to {Cm(targetCm)}...");
    }

    /// <summary>Returns null when the move succeeded, otherwise the reason it did not.</summary>
    async Task<string?> RunMoveAsync(double targetCm, string statusText, TimeSpan? timeout = null)
    {
        await _gate.WaitAsync();
        SetBusy(true, targetCm);
        _move = new CancellationTokenSource();
        SetStatus(statusText);
        string? failure = null;
        try
        {
            await Desk.MoveToAsync(targetCm, _move.Token, timeout);
            SetStatus($"At {Cm(targetCm)}.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped.");
            failure = "The move was stopped.";
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            failure = ex.Message;
        }
        finally
        {
            _move.Dispose();
            _move = null;
            SetBusy(false, null);
            _gate.Release();
        }

        return failure;
    }

    // ---- presets and schedule ----------------------------------------------

    public void SavePreset(int slot)
    {
        if (Desk.CurrentCm is not { } current)
        {
            SetStatus("Connect to the desk first.");
            return;
        }

        var rounded = Math.Round(current, 1);
        if (slot == 1) Settings.Preset1 = rounded;
        else Settings.Preset2 = rounded;

        Settings.Save();
        PresetsChanged?.Invoke();
        SetStatus($"Preset {slot} saved at {Cm(rounded)}.");
    }

    public double Preset(int slot) => slot == 2 ? Settings.Preset2 : Settings.Preset1;

    /// <summary>"At Preset 1" when the desk is sitting at one of the presets.</summary>
    public string? PresetNote => Desk.CurrentCm switch
    {
        { } cm when Math.Abs(cm - Settings.Preset1) < 0.5 => "At Preset 1",
        { } cm when Math.Abs(cm - Settings.Preset2) < 0.5 => "At Preset 2",
        _ => null
    };

    public void SetScheduleEnabled(bool enabled)
    {
        Settings.ScheduleEnabled = enabled;
        Scheduler.Enabled = enabled;
        Settings.Save();
        SetStatus(enabled
            ? "Schedule running. The first move happens at the next boundary."
            : "Schedule stopped.");
    }

    public void SetSchedule(WeekSchedule schedule)
    {
        Settings.Schedule = schedule;
        Scheduler.Schedule = schedule;
        Settings.Save();
        SetStatus("Schedule saved.");
    }

    /// <summary>"Next: up at 09:50." for the schedule card.</summary>
    public string NextMoveText
    {
        get
        {
            if (!Settings.ScheduleEnabled) return "Not running.";
            if (Scheduler.NextMove is not { } next) return "No days are switched on.";

            var when = next.When.Date == DateTime.Today
                ? next.When.ToString("HH:mm", CultureInfo.InvariantCulture)
                : next.When.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
            var what = next.To == DeskState.Stand ? "up" : "back down";
            return $"Next: {what} at {when}.";
        }
    }

    // ---- requests from perch-cli -------------------------------------------

    /// <summary>Hands a CLI request to the UI thread and waits for the answer. Safe to call from any thread.</summary>
    public Task<ControlResponse> HandleControlAsync(ControlRequest request)
    {
        var completion = new TaskCompletionSource<ControlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        Post(async () =>
        {
            try
            {
                completion.SetResult(await ExecuteControlAsync(request));
            }
            catch (Exception ex)
            {
                completion.SetResult(new ControlResponse(false, Error: ex.Message, Code: ExitCodes.MoveFailed));
            }
        });

        return completion.Task;
    }

    async Task<ControlResponse> ExecuteControlAsync(ControlRequest request)
    {
        if (!await EnsureConnectedAsync())
            return new ControlResponse(false, Error:
                "The Perch app is not connected to a desk. Open it and connect, or run the command with --direct.",
                Code: ExitCodes.NoConnect);

        switch (request.Command)
        {
            case "status":
                return new ControlResponse(true, await Desk.ReadHeightAsync(), Device: Desk.DeviceName);

            case "stop":
                await StopAsync();
                return new ControlResponse(true, Desk.CurrentCm, Device: Desk.DeviceName);

            case "set" or "nudge" or "preset":
            {
                double target;
                switch (request.Command)
                {
                    case "set":
                        if (request.HeightCm is not { } height)
                            return new ControlResponse(false, Error: "set needs a height.", Code: ExitCodes.Usage);
                        target = height;
                        break;

                    case "nudge":
                        target = (Desk.CurrentCm ?? await Desk.ReadHeightAsync()) + (request.DeltaCm ?? 0);
                        break;

                    default:
                        target = Preset(request.Slot ?? 1);
                        break;
                }

                target = Clamp(target);
                TargetShown?.Invoke(target);

                var timeout = request.TimeoutSeconds is { } seconds && seconds > 0
                    ? TimeSpan.FromSeconds(seconds)
                    : (TimeSpan?)null;

                var failure = await RunMoveAsync(target, $"perch-cli: moving to {Cm(target)}...", timeout);
                return failure is null
                    ? new ControlResponse(true, Desk.CurrentCm ?? target, target, Desk.DeviceName)
                    : new ControlResponse(false, Desk.CurrentCm, target, Desk.DeviceName, failure, ExitCodes.MoveFailed);
            }

            default:
                return new ControlResponse(false, Error: $"Unknown command \"{request.Command}\".", Code: ExitCodes.Usage);
        }
    }

    // ---- plumbing ----------------------------------------------------------

    public static double Clamp(double cm) => Math.Clamp(cm, DeskController.MinCm, DeskController.MaxCm);

    public static string Cm(double cm) => $"{cm.ToString("0.0", CultureInfo.CurrentCulture)} cm";

    public void SetStatus(string text)
    {
        Status = text;
        StatusChanged?.Invoke(text);
    }

    void SetConnection(ConnectionState state, string text)
    {
        Connection = state;
        ConnectionText = text;
        ConnectionChanged?.Invoke();
    }

    void SetOffline(string status)
    {
        SetConnection(ConnectionState.Offline, "Not connected");
        SetStatus(status);
    }

    void SetBusy(bool busy, double? target)
    {
        IsBusy = busy;
        MoveTarget = target;
        BusyChanged?.Invoke();
    }

    void Post(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context) action();
        else _context.Post(_ => action(), null);
    }

    void Post(Func<Task> action) => Post(() => { _ = action(); });

    public void Dispose()
    {
        _move?.Cancel();
        Scheduler.Dispose();
        Desk.Dispose();
    }
}
