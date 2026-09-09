using System.Globalization;

namespace IdasenDeskControl;

public sealed class MainForm : Form
{
    sealed record DeviceItem(string Name, string Id)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
    }

    readonly DeskController _desk = new();
    readonly Settings _settings = Settings.Load();
    readonly Scheduler _scheduler = new();

    /// <summary>One move at a time; a scheduled move queues behind a manual one.</summary>
    readonly SemaphoreSlim _gate = new(1, 1);
    CancellationTokenSource? _move;

    readonly ComboBox _devices = new();
    readonly Button _scan = new();
    readonly Button _connect = new();
    readonly Label _height = new();
    readonly Label _subtitle = new();
    readonly NumericUpDown _target = new();
    readonly Button _go = new();
    readonly Button _stop = new();
    readonly Button _up = new();
    readonly Button _down = new();
    readonly Button _preset1 = new();
    readonly Button _preset2 = new();
    readonly Button _save1 = new();
    readonly Button _save2 = new();
    readonly CheckBox _scheduleOn = new();
    readonly Button _editSchedule = new();
    readonly Label _nextMove = new();
    readonly StatusStrip _statusStrip = new();
    readonly ToolStripStatusLabel _status = new();

    public MainForm()
    {
        Text = "Idasen Desk Control";
        ClientSize = new Size(420, 476);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        WireEvents();
        RefreshPresetLabels();

        _desk.HeightChanged += cm => OnUi(() => ShowHeight(cm));
        _desk.ConnectionChanged += connected => OnUi(() =>
        {
            if (!connected) SetDisconnectedUi("The desk dropped the Bluetooth connection.");
        });

        _scheduler.Schedule = _settings.Schedule;
        _scheduler.Enabled = _settings.ScheduleEnabled;
        _scheduleOn.Checked = _settings.ScheduleEnabled;
        _scheduler.MoveRequested += (cm, reason) => OnUi(async () => await RunScheduledMoveAsync(cm, reason));
        _scheduler.Ticked += () => OnUi(ShowNextMove);
        ShowNextMove();
    }

    void BuildLayout()
    {
        var deskLabel = new Label { Text = "Desk:", Location = new Point(12, 14), AutoSize = true };

        _devices.Location = new Point(52, 10);
        _devices.Size = new Size(194, 23);
        _devices.DropDownStyle = ComboBoxStyle.DropDownList;

        _scan.Text = "Scan";
        _scan.Location = new Point(250, 9);
        _scan.Size = new Size(56, 25);

        _connect.Text = "Connect";
        _connect.Location = new Point(312, 9);
        _connect.Size = new Size(96, 25);

        _height.Text = "--.- cm";
        _height.Location = new Point(12, 48);
        _height.Size = new Size(396, 58);
        _height.TextAlign = ContentAlignment.MiddleCenter;
        _height.Font = new Font("Segoe UI", 30F, FontStyle.Regular);

        _subtitle.Text = "Not connected";
        _subtitle.Location = new Point(12, 108);
        _subtitle.Size = new Size(396, 20);
        _subtitle.TextAlign = ContentAlignment.MiddleCenter;
        _subtitle.ForeColor = SystemColors.GrayText;

        var moveBox = new GroupBox
        {
            Text = "Move to",
            Location = new Point(12, 138),
            Size = new Size(396, 92)
        };

        _target.Location = new Point(16, 28);
        _target.Size = new Size(88, 29);
        _target.Font = new Font("Segoe UI", 12F);
        _target.DecimalPlaces = 1;
        _target.Increment = 0.5M;
        _target.Minimum = (decimal)DeskController.MinCm;
        _target.Maximum = (decimal)DeskController.MaxCm;
        _target.Value = Clamp((decimal)_settings.LastTarget);

        var cmLabel = new Label { Text = "cm", Location = new Point(110, 35), AutoSize = true };

        _go.Text = "Go";
        _go.Location = new Point(142, 27);
        _go.Size = new Size(90, 31);

        _stop.Text = "Stop";
        _stop.Location = new Point(238, 27);
        _stop.Size = new Size(90, 31);

        _up.Text = "+1";
        _up.Location = new Point(338, 20);
        _up.Size = new Size(42, 24);

        _down.Text = "-1";
        _down.Location = new Point(338, 48);
        _down.Size = new Size(42, 24);

        moveBox.Controls.AddRange(new Control[] { _target, cmLabel, _go, _stop, _up, _down });

        var presetBox = new GroupBox
        {
            Text = "Presets",
            Location = new Point(12, 240),
            Size = new Size(396, 118)
        };

        _preset1.Location = new Point(16, 26);
        _preset1.Size = new Size(180, 34);
        _save1.Text = "Save current here";
        _save1.Location = new Point(206, 26);
        _save1.Size = new Size(172, 34);

        _preset2.Location = new Point(16, 68);
        _preset2.Size = new Size(180, 34);
        _save2.Text = "Save current here";
        _save2.Location = new Point(206, 68);
        _save2.Size = new Size(172, 34);

        presetBox.Controls.AddRange(new Control[] { _preset1, _save1, _preset2, _save2 });

        var scheduleBox = new GroupBox
        {
            Text = "Schedule",
            Location = new Point(12, 368),
            Size = new Size(396, 82)
        };

        _scheduleOn.Text = "Run the schedule";
        _scheduleOn.Location = new Point(16, 26);
        _scheduleOn.Size = new Size(160, 26);

        _editSchedule.Text = "Edit schedule...";
        _editSchedule.Location = new Point(206, 22);
        _editSchedule.Size = new Size(172, 30);

        _nextMove.Location = new Point(16, 56);
        _nextMove.Size = new Size(362, 18);
        _nextMove.ForeColor = SystemColors.GrayText;

        scheduleBox.Controls.AddRange(new Control[] { _scheduleOn, _editSchedule, _nextMove });

        _status.Text = "Ready.";
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _statusStrip.Items.Add(_status);

        Controls.AddRange(new Control[]
        {
            deskLabel, _devices, _scan, _connect, _height, _subtitle,
            moveBox, presetBox, scheduleBox, _statusStrip
        });

        AcceptButton = _go;
    }

    void WireEvents()
    {
        Load += async (_, _) => await ScanAsync(autoConnect: true);
        _scan.Click += async (_, _) => await ScanAsync(autoConnect: false);
        _connect.Click += async (_, _) => await ToggleConnectionAsync();
        _go.Click += async (_, _) => await MoveToAsync((double)_target.Value);
        _stop.Click += async (_, _) => await StopAsync();
        _up.Click += async (_, _) => await NudgeAsync(+1.0);
        _down.Click += async (_, _) => await NudgeAsync(-1.0);
        _preset1.Click += async (_, _) => await MoveToAsync(_settings.Preset1);
        _preset2.Click += async (_, _) => await MoveToAsync(_settings.Preset2);
        _save1.Click += (_, _) => SavePreset(1);
        _save2.Click += (_, _) => SavePreset(2);
        _scheduleOn.CheckedChanged += (_, _) => ToggleSchedule();
        _editSchedule.Click += (_, _) => EditSchedule();
        FormClosing += (_, _) =>
        {
            _move?.Cancel();
            _settings.LastTarget = (double)_target.Value;
            _settings.Save();
            _scheduler.Dispose();
            _desk.Dispose();
        };
    }

    // ---- devices -----------------------------------------------------------

    async Task ScanAsync(bool autoConnect)
    {
        _scan.Enabled = false;
        _status.Text = "Looking for paired Bluetooth devices...";
        try
        {
            var devices = await DeskController.ListPairedDevicesAsync();
            _devices.Items.Clear();
            foreach (var device in devices)
                _devices.Items.Add(new DeviceItem(device.Name, device.Id));

            if (_devices.Items.Count == 0)
            {
                _status.Text = "No paired Bluetooth LE devices. Pair the desk in Windows Bluetooth settings first.";
                return;
            }

            var pick = PickDevice();
            if (pick >= 0) _devices.SelectedIndex = pick;

            _status.Text = $"Found {_devices.Items.Count} paired device(s).";

            if (autoConnect && pick >= 0 && ((DeviceItem)_devices.Items[pick]!).Id == _settings.DeviceId)
                await ToggleConnectionAsync();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _scan.Enabled = true;
        }
    }

    /// <summary>Saved device first, otherwise the first thing that looks like a desk.</summary>
    int PickDevice()
    {
        for (var i = 0; i < _devices.Items.Count; i++)
            if (((DeviceItem)_devices.Items[i]!).Id == _settings.DeviceId)
                return i;

        for (var i = 0; i < _devices.Items.Count; i++)
        {
            var name = ((DeviceItem)_devices.Items[i]!).Name;
            if (name.Contains("desk", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("lift", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("linak", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return _devices.Items.Count > 0 ? 0 : -1;
    }

    async Task ToggleConnectionAsync()
    {
        if (_desk.IsConnected)
        {
            _desk.Disconnect();
            SetDisconnectedUi("Disconnected.");
            return;
        }

        if (_devices.SelectedItem is not DeviceItem item)
        {
            _status.Text = "Pick a device first.";
            return;
        }

        _connect.Enabled = false;
        _status.Text = $"Connecting to {item.Name}...";
        try
        {
            await _desk.ConnectAsync(item.Id);
            _settings.DeviceId = item.Id;
            _settings.DeviceName = item.Name;
            _settings.Save();

            _connect.Text = "Disconnect";
            _subtitle.Text = $"Connected to {item.Name}";
            _status.Text = "Connected.";
        }
        catch (Exception ex)
        {
            _desk.Disconnect();
            SetDisconnectedUi(ex.Message);
        }
        finally
        {
            _connect.Enabled = true;
        }
    }

    /// <summary>Reconnects to the saved desk, which a scheduled move needs after a sleep or a drop.</summary>
    async Task<bool> EnsureConnectedAsync()
    {
        if (_desk.IsConnected) return true;
        if (_settings.DeviceId is null) return false;

        try
        {
            await _desk.ConnectAsync(_settings.DeviceId);
            _connect.Text = "Disconnect";
            _subtitle.Text = $"Connected to {_settings.DeviceName ?? "desk"}";
            return true;
        }
        catch (Exception ex)
        {
            _desk.Disconnect();
            SetDisconnectedUi(ex.Message);
            return false;
        }
    }

    // ---- movement ----------------------------------------------------------

    async Task MoveToAsync(double targetCm)
    {
        if (!_desk.IsConnected)
        {
            _status.Text = "Connect to the desk first.";
            return;
        }

        _target.Value = Clamp((decimal)targetCm);
        await RunMoveAsync(targetCm, $"Moving to {Cm(targetCm)}...");
    }

    async Task RunScheduledMoveAsync(double targetCm, string reason)
    {
        if (!await EnsureConnectedAsync())
        {
            _status.Text = $"Skipped the {reason}: no connection to the desk.";
            return;
        }

        _target.Value = Clamp((decimal)targetCm);
        await RunMoveAsync(targetCm, $"{char.ToUpperInvariant(reason[0])}{reason[1..]}: moving to {Cm(targetCm)}...");
    }

    async Task RunMoveAsync(double targetCm, string statusText)
    {
        await _gate.WaitAsync();
        SetBusy(true);
        _move = new CancellationTokenSource();
        _status.Text = statusText;
        try
        {
            await _desk.MoveToAsync(targetCm, _move.Token);
            _status.Text = $"At {Cm(targetCm)}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _move.Dispose();
            _move = null;
            SetBusy(false);
            _gate.Release();
        }
    }

    async Task NudgeAsync(double deltaCm)
    {
        if (_desk.CurrentCm is not { } current)
        {
            _status.Text = "Connect to the desk first.";
            return;
        }
        await MoveToAsync(current + deltaCm);
    }

    async Task StopAsync()
    {
        _move?.Cancel();
        try
        {
            await _desk.StopAsync();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    // ---- presets and schedule ----------------------------------------------

    void SavePreset(int slot)
    {
        if (_desk.CurrentCm is not { } current)
        {
            _status.Text = "Connect to the desk first.";
            return;
        }

        var rounded = Math.Round(current, 1);
        if (slot == 1) _settings.Preset1 = rounded;
        else _settings.Preset2 = rounded;

        _settings.Save();
        RefreshPresetLabels();
        _status.Text = $"Preset {slot} saved at {Cm(rounded)}.";
    }

    void RefreshPresetLabels()
    {
        _preset1.Text = $"Preset 1  -  {Cm(_settings.Preset1)}";
        _preset2.Text = $"Preset 2  -  {Cm(_settings.Preset2)}";
    }

    void ToggleSchedule()
    {
        _settings.ScheduleEnabled = _scheduleOn.Checked;
        _scheduler.Enabled = _scheduleOn.Checked;
        _settings.Save();
        _status.Text = _scheduleOn.Checked
            ? "Schedule running. The first move happens at the next boundary."
            : "Schedule stopped.";
        ShowNextMove();
    }

    void EditSchedule()
    {
        using var dialog = new ScheduleForm(_settings.Schedule);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _settings.Schedule = dialog.Schedule;
        _scheduler.Schedule = dialog.Schedule;
        _settings.Save();
        _status.Text = "Schedule saved.";
        ShowNextMove();
    }

    void ShowNextMove()
    {
        if (!_scheduleOn.Checked)
        {
            _nextMove.Text = "Not running.";
            return;
        }

        if (_scheduler.NextMove is not { } next)
        {
            _nextMove.Text = "No days are switched on.";
            return;
        }

        var when = next.When.Date == DateTime.Today
            ? next.When.ToString("HH:mm", CultureInfo.InvariantCulture)
            : next.When.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
        var what = next.To == DeskState.Stand ? "up" : "back down";
        _nextMove.Text = $"Next: {what} at {when}.";
    }

    // ---- plumbing ----------------------------------------------------------

    void ShowHeight(double cm) => _height.Text = Cm(cm);

    static string Cm(double cm) => $"{cm.ToString("0.0", CultureInfo.InvariantCulture)} cm";

    void SetDisconnectedUi(string status)
    {
        _connect.Text = "Connect";
        _subtitle.Text = "Not connected";
        _height.Text = "--.- cm";
        _status.Text = status;
    }

    void SetBusy(bool busy)
    {
        _go.Enabled = !busy;
        _up.Enabled = !busy;
        _down.Enabled = !busy;
        _preset1.Enabled = !busy;
        _preset2.Enabled = !busy;
        _connect.Enabled = !busy;
        _scan.Enabled = !busy;
    }

    static decimal Clamp(decimal cm) =>
        Math.Clamp(cm, (decimal)DeskController.MinCm, (decimal)DeskController.MaxCm);

    void OnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
