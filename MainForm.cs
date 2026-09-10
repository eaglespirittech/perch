using System.Diagnostics;
using System.Globalization;
using Perch.Ipc;
using Perch.Ui;

namespace Perch;

public sealed class MainForm : Form
{
    sealed record DeviceItem(string Name, string Id);

    readonly DeskController _desk = new();
    readonly Settings _settings = Settings.Load();
    readonly Scheduler _scheduler = new();

    /// <summary>One move at a time; a scheduled move queues behind a manual one.</summary>
    readonly SemaphoreSlim _gate = new(1, 1);
    CancellationTokenSource? _move;

    readonly List<DeviceItem> _devices = new();
    DeviceItem? _selected;

    readonly Label _title = new();
    readonly Label _subtitle = new();
    readonly PerchButton _menu = new();
    readonly HeightGauge _gauge = new();
    readonly Card _moveCard = new();
    readonly Card _field = new();
    readonly TextBox _target = new();
    readonly Label _fieldUnit = new();
    readonly PerchButton _less = new();
    readonly PerchButton _more = new();
    readonly PerchButton _go = new();
    readonly PerchButton _stop = new();
    readonly Card _presetCard = new();
    readonly PerchButton _preset1 = new();
    readonly PerchButton _preset2 = new();
    readonly PerchButton _save1 = new();
    readonly PerchButton _save2 = new();
    readonly Card _scheduleCard = new();
    readonly ToggleSwitch _scheduleOn = new();
    readonly Label _scheduleLabel = new();
    readonly PerchButton _editSchedule = new();
    readonly Label _nextMove = new();
    readonly Label _status = new();

    readonly ControlServer _control;
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _startup = new();
    Icon? _appIcon;
    bool _startHidden;
    bool _exiting;

    public MainForm(bool startHidden = false)
    {
        _startHidden = startHidden;

        Text = "Perch";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Body;
        BackColor = Theme.Window;

        BuildLayout();
        WireEvents();
        BuildTrayIcon();

        // While the app runs it owns the desk's single Bluetooth connection, so it
        // answers on behalf of perch-cli rather than making the CLI fight it for one.
        _control = new ControlServer(HandleControlAsync);
        RefreshPresetLabels();
        ShowTarget(_settings.LastTarget);

        // The window may never be shown, but BLE callbacks still need something to
        // marshal onto, so make the handle exist up front.
        _ = Handle;

        _startup.Interval = 100;
        _startup.Tick += async (_, _) =>
        {
            _startup.Stop();
            await ScanAsync(autoConnect: true);
        };
        _startup.Start();

        _desk.HeightChanged += cm => OnUi(() =>
        {
            _gauge.Value = cm;
            _tray.Text = $"Perch - {Cm(cm)}";
        });
        _desk.ConnectionChanged += connected => OnUi(() =>
        {
            if (!connected) SetDisconnectedUi("The desk dropped the Bluetooth connection.");
        });

        _scheduler.Schedule = _settings.Schedule;
        _scheduler.Enabled = _settings.ScheduleEnabled;
        _scheduleOn.SetCheckedSilently(_settings.ScheduleEnabled);
        _scheduler.MoveRequested += (cm, reason) => OnUi(async () => await RunScheduledMoveAsync(cm, reason));
        _scheduler.Ticked += () => OnUi(ShowNextMove);
        ShowNextMove();

        Theme.Changed += () => OnUi(ApplyTheme);
    }

    // ---- layout ------------------------------------------------------------

    const int Gutter = 20;
    const int Width_ = 420;
    const int Content = Width_ - Gutter * 2;
    const int Pad = 16;

    void BuildLayout()
    {
        var y = 18;

        Style(_title, "Perch", Theme.Title, Theme.Text, new Point(Gutter, y));
        _menu.Text = "";
        _menu.IsGlyph = true;
        _menu.Kind = ButtonKind.Subtle;
        _menu.Bounds = new Rectangle(Width_ - Gutter - 34, y, 34, 34);

        y += 34;
        Style(_subtitle, "Not connected", Theme.Body, Theme.TextSecondary, new Point(Gutter, y));

        y += 30;
        _gauge.Bounds = new Rectangle(Gutter, y, Content, 132);

        y += 132 + 12;
        _moveCard.Bounds = new Rectangle(Gutter, y, Content, 88);
        BuildMoveCard();

        y += 88 + 12;
        _presetCard.Bounds = new Rectangle(Gutter, y, Content, 124);
        BuildPresetCard();

        y += 124 + 12;
        _scheduleCard.Bounds = new Rectangle(Gutter, y, Content, 90);
        BuildScheduleCard();

        y += 90 + 14;
        Style(_status, "Ready.", Theme.Caption, Theme.TextSecondary, new Point(Gutter, y));
        _status.AutoSize = false;
        _status.Size = new Size(Content, 42);

        Controls.AddRange(new Control[]
        {
            _title, _subtitle, _menu, _gauge, _moveCard, _presetCard, _scheduleCard, _status
        });

        ClientSize = new Size(Width_, y + 42 + 12);
        AcceptButton = _go;
    }

    void BuildMoveCard()
    {
        _field.Fill = Theme.Field;
        _field.Radius = Theme.ControlRadius;
        _field.Bounds = new Rectangle(Pad + 40, 24, 110, 40);

        _target.BorderStyle = BorderStyle.None;
        _target.TextAlign = HorizontalAlignment.Center;
        _target.Font = Theme.Value;
        _target.BackColor = Theme.Field;
        _target.ForeColor = Theme.Text;
        _target.Bounds = new Rectangle(8, 10, 62, 22);

        Style(_fieldUnit, "cm", Theme.Caption, Theme.TextSecondary, new Point(74, 15));
        _fieldUnit.BackColor = Theme.Field;

        _field.Controls.AddRange(new Control[] { _target, _fieldUnit });

        _less.Text = ""; // minus
        _more.Text = ""; // plus
        foreach (var button in new[] { _less, _more })
        {
            button.IsGlyph = true;
            button.Kind = ButtonKind.Standard;
        }

        _less.Bounds = new Rectangle(Pad, 24, 36, 40);
        _more.Bounds = new Rectangle(Pad + 154, 24, 36, 40);

        _go.Text = "Go";
        _go.Kind = ButtonKind.Primary;
        _go.Bounds = new Rectangle(Pad + 200, 24, 76, 40);

        _stop.Text = "Stop";
        _stop.Bounds = new Rectangle(Pad + 284, 24, 64, 40);

        _moveCard.Controls.AddRange(new Control[] { _less, _field, _more, _go, _stop });
    }

    void BuildPresetCard()
    {
        _preset1.Bounds = new Rectangle(Pad, 18, 196, 40);
        _preset2.Bounds = new Rectangle(Pad, 66, 196, 40);

        _save1.Bounds = new Rectangle(Pad + 204, 18, 144, 40);
        _save2.Bounds = new Rectangle(Pad + 204, 66, 144, 40);

        foreach (var button in new[] { _save1, _save2 })
        {
            button.Text = "Save current";
            button.Kind = ButtonKind.Subtle;
        }

        _presetCard.Controls.AddRange(new Control[] { _preset1, _preset2, _save1, _save2 });
    }

    void BuildScheduleCard()
    {
        _scheduleOn.Bounds = new Rectangle(Pad, 22, 44, 22);

        Style(_scheduleLabel, "Run the schedule", Theme.Body, Theme.Text, new Point(Pad + 56, 24));
        _scheduleLabel.BackColor = Theme.Surface;

        _editSchedule.Text = "Edit schedule";
        _editSchedule.Bounds = new Rectangle(Pad + 232, 18, 116, 34);

        Style(_nextMove, "Not running.", Theme.Caption, Theme.TextSecondary, new Point(Pad, 58));
        _nextMove.BackColor = Theme.Surface;

        _scheduleCard.Controls.AddRange(new Control[] { _scheduleOn, _scheduleLabel, _editSchedule, _nextMove });
    }

    static void Style(Label label, string text, Font font, Color color, Point at)
    {
        label.Text = text;
        label.Font = font;
        label.ForeColor = color;
        label.Location = at;
        label.AutoSize = true;
        label.BackColor = Color.Transparent;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowTrim(this);
    }

    void ApplyTheme()
    {
        BackColor = Theme.Window;
        Font = Theme.Body;

        _title.ForeColor = Theme.Text;
        _subtitle.ForeColor = Theme.TextSecondary;
        _status.ForeColor = Theme.TextSecondary;
        _scheduleLabel.ForeColor = Theme.Text;
        _scheduleLabel.BackColor = Theme.Surface;
        _nextMove.ForeColor = Theme.TextSecondary;
        _nextMove.BackColor = Theme.Surface;

        _field.Fill = Theme.Field;
        _target.BackColor = Theme.Field;
        _target.ForeColor = Theme.Text;
        _fieldUnit.BackColor = Theme.Field;
        _fieldUnit.ForeColor = Theme.TextSecondary;

        ToolStripManager.Renderer = new FluentMenuRenderer();

        var previous = _appIcon;
        _appIcon = AppIcon.Create();
        Icon = _appIcon;
        _tray.Icon = _appIcon;
        previous?.Dispose();

        Theme.ApplyWindowTrim(this);
        Invalidate(true);
    }

    // ---- wiring ------------------------------------------------------------

    void WireEvents()
    {
        _menu.Click += (_, _) => ShowMenu();
        _go.Click += async (_, _) => await MoveToAsync(ReadTarget());
        _stop.Click += async (_, _) => await StopAsync();
        _less.Click += (_, _) => ShowTarget(ReadTarget() - 0.5);
        _more.Click += (_, _) => ShowTarget(ReadTarget() + 0.5);
        _preset1.Click += async (_, _) => await MoveToAsync(_settings.Preset1);
        _preset2.Click += async (_, _) => await MoveToAsync(_settings.Preset2);
        _save1.Click += (_, _) => SavePreset(1);
        _save2.Click += (_, _) => SavePreset(2);
        _scheduleOn.CheckedChanged += (_, _) => ToggleSchedule();
        _editSchedule.Click += (_, _) => EditSchedule();
        _target.Leave += (_, _) => ShowTarget(ReadTarget());

        FormClosing += (_, e) =>
        {
            // Closing the window parks the app next to the clock; the schedule only works
            // while it is running. Exit is on the tray menu and the overflow menu.
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _move?.Cancel();
            _settings.LastTarget = ReadTarget();
            _settings.Save();
            _startup.Dispose();
            _scheduler.Dispose();
            _desk.Dispose();
            _control.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _appIcon?.Dispose();
        };
    }

    // ---- notification area -------------------------------------------------

    void BuildTrayIcon()
    {
        _appIcon = AppIcon.Create();
        Icon = _appIcon;

        var menu = new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ShowImageMargin = false
        };
        menu.Items.Add("Open Perch", null, (_, _) => RestoreWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Perch", null, (_, _) => ExitApp());

        _tray.Icon = _appIcon;
        _tray.Text = "Perch";
        _tray.ContextMenuStrip = menu;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreWindow();
    }

    /// <summary>Suppresses the very first Show when Windows started us at sign-in.</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && value)
        {
            _startHidden = false;
            base.SetVisibleCore(false);
            return;
        }

        base.SetVisibleCore(value);
    }

    void HideToTray()
    {
        Hide();

        // Closing used to be the moment settings were written; keep that true now that
        // it no longer ends the process.
        _settings.LastTarget = ReadTarget();
        _settings.Save();

        if (_settings.TrayHintShown) return;
        _settings.TrayHintShown = true;
        _settings.Save();
        _tray.ShowBalloonTip(4000, "Perch is still running",
            "It sits by the clock so the schedule keeps working. Right-click the icon to exit.",
            ToolTipIcon.Info);
    }

    void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        _exiting = true;
        Close();
    }

    void ShowMenu()
    {
        var menu = new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ShowImageMargin = false,
            ShowCheckMargin = true
        };

        var desks = new ToolStripMenuItem("Desk");
        StyleDropDown(desks.DropDown);

        if (_devices.Count == 0)
        {
            desks.DropDownItems.Add(new ToolStripMenuItem("No paired devices") { Enabled = false });
        }
        else
        {
            foreach (var device in _devices)
            {
                var item = new ToolStripMenuItem(device.Name) { Checked = device.Id == _selected?.Id };
                var captured = device;
                item.Click += async (_, _) => await SelectDeviceAsync(captured);
                desks.DropDownItems.Add(item);
            }
        }

        desks.DropDownItems.Add(new ToolStripSeparator());
        desks.DropDownItems.Add("Scan again", null, async (_, _) => await ScanAsync(autoConnect: false));
        menu.Items.Add(desks);

        menu.Items.Add(_desk.IsConnected ? "Disconnect" : "Connect", null,
            async (_, _) => await ToggleConnectionAsync());

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Nudge up 1 cm", null, async (_, _) => await NudgeAsync(+1.0));
        menu.Items.Add("Nudge down 1 cm", null, async (_, _) => await NudgeAsync(-1.0));

        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = AutoStart.IsEnabled };
        startup.Click += (_, _) => ToggleAutoStart(!startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open settings folder", null, (_, _) => OpenSettingsFolder());
        menu.Items.Add("Exit Perch", null, (_, _) => ExitApp());

        StyleDropDown(menu);
        menu.Show(_menu, new Point(_menu.Width, _menu.Height), ToolStripDropDownDirection.BelowLeft);
    }

    static void StyleDropDown(ToolStripDropDown drop)
    {
        drop.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        drop.BackColor = Theme.Surface;
        drop.ForeColor = Theme.Text;
        drop.Font = Theme.Body;
    }

    void ToggleAutoStart(bool enabled)
    {
        var error = AutoStart.SetEnabled(enabled);
        _status.Text = error is not null
            ? $"Could not change the startup setting: {error}"
            : enabled
                ? "Perch will start when you sign in to Windows."
                : "Perch will no longer start automatically.";
    }

    void OpenSettingsFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Settings.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    // ---- devices -----------------------------------------------------------

    async Task ScanAsync(bool autoConnect)
    {
        _status.Text = "Looking for paired Bluetooth devices...";
        try
        {
            var found = await DeskController.ListPairedDevicesAsync();
            _devices.Clear();
            foreach (var device in found)
                _devices.Add(new DeviceItem(device.Name, device.Id));

            if (_devices.Count == 0)
            {
                _status.Text = "No paired Bluetooth LE devices. Pair the desk in Windows Bluetooth settings first.";
                return;
            }

            _selected = PickDevice();
            _status.Text = $"Found {_devices.Count} paired device(s).";

            if (autoConnect && _selected is { } pick && pick.Id == _settings.DeviceId)
                await ToggleConnectionAsync();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    /// <summary>Saved device first, otherwise the first thing that looks like a desk.</summary>
    DeviceItem? PickDevice()
    {
        var saved = _devices.FirstOrDefault(d => d.Id == _settings.DeviceId);
        if (saved is not null) return saved;

        var looksRight = _devices.FirstOrDefault(d =>
            d.Name.Contains("desk", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("lift", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("linak", StringComparison.OrdinalIgnoreCase));

        return looksRight ?? _devices.FirstOrDefault();
    }

    async Task SelectDeviceAsync(DeviceItem device)
    {
        if (_desk.IsConnected) _desk.Disconnect();
        _selected = device;
        await ToggleConnectionAsync();
    }

    async Task ToggleConnectionAsync()
    {
        if (_desk.IsConnected)
        {
            _desk.Disconnect();
            SetDisconnectedUi("Disconnected.");
            return;
        }

        if (_selected is not { } item)
        {
            _status.Text = "Pick a desk from the menu first.";
            return;
        }

        _status.Text = $"Connecting to {item.Name}...";
        try
        {
            await _desk.ConnectAsync(item.Id);
            _settings.DeviceId = item.Id;
            _settings.DeviceName = item.Name;
            _settings.Save();

            _subtitle.Text = $"Connected to {item.Name}";
            _status.Text = "Connected.";
        }
        catch (Exception ex)
        {
            _desk.Disconnect();
            SetDisconnectedUi(ex.Message);
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
            _status.Text = "Connect to the desk first, from the menu at the top right.";
            return;
        }

        ShowTarget(targetCm);
        await RunMoveAsync(targetCm, $"Moving to {Cm(targetCm)}...");
    }

    async Task RunScheduledMoveAsync(double targetCm, string reason)
    {
        if (!await EnsureConnectedAsync())
        {
            _status.Text = $"Skipped the {reason}: no connection to the desk.";
            return;
        }

        ShowTarget(targetCm);
        await RunMoveAsync(targetCm, $"{char.ToUpperInvariant(reason[0])}{reason[1..]}: moving to {Cm(targetCm)}...");
    }

    /// <summary>Returns null when the move succeeded, otherwise the reason it did not.</summary>
    async Task<string?> RunMoveAsync(double targetCm, string statusText, TimeSpan? timeout = null)
    {
        await _gate.WaitAsync();
        SetBusy(true);
        _gauge.Target = targetCm;
        _move = new CancellationTokenSource();
        _status.Text = statusText;
        string? failure = null;
        try
        {
            await _desk.MoveToAsync(targetCm, _move.Token, timeout);
            _status.Text = $"At {Cm(targetCm)}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Stopped.";
            failure = "The move was stopped.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            failure = ex.Message;
        }
        finally
        {
            _move.Dispose();
            _move = null;
            _gauge.Target = null;
            SetBusy(false);
            _gate.Release();
        }

        return failure;
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
        _preset1.Text = $"Preset 1   ·   {Cm(_settings.Preset1)}";
        _preset2.Text = $"Preset 2   ·   {Cm(_settings.Preset2)}";
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

    // ---- requests from perch-cli -------------------------------------------

    Task<ControlResponse> HandleControlAsync(ControlRequest request)
    {
        var completion = new TaskCompletionSource<ControlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        OnUi(async () =>
        {
            try
            {
                completion.SetResult(await ExecuteControlAsync(request));
            }
            catch (Exception ex)
            {
                completion.SetResult(new ControlResponse(false, Error: ex.Message, Code: 4));
            }
        });

        return completion.Task;
    }

    async Task<ControlResponse> ExecuteControlAsync(ControlRequest request)
    {
        if (!await EnsureConnectedAsync())
            return new ControlResponse(false, Error:
                "The Perch app is not connected to a desk. Open it and connect, or run the command with --direct.",
                Code: 3);

        switch (request.Command)
        {
            case "status":
                return new ControlResponse(true, await _desk.ReadHeightAsync(), Device: _desk.DeviceName);

            case "stop":
                await StopAsync();
                return new ControlResponse(true, _desk.CurrentCm, Device: _desk.DeviceName);

            case "set" or "nudge" or "preset":
            {
                double target;
                switch (request.Command)
                {
                    case "set":
                        if (request.HeightCm is not { } height)
                            return new ControlResponse(false, Error: "set needs a height.", Code: 1);
                        target = height;
                        break;

                    case "nudge":
                        target = (_desk.CurrentCm ?? await _desk.ReadHeightAsync()) + (request.DeltaCm ?? 0);
                        break;

                    default:
                        target = request.Slot == 2 ? _settings.Preset2 : _settings.Preset1;
                        break;
                }

                target = Clamp(target);
                ShowTarget(target);

                var timeout = request.TimeoutSeconds is { } seconds && seconds > 0
                    ? TimeSpan.FromSeconds(seconds)
                    : (TimeSpan?)null;

                var failure = await RunMoveAsync(target, $"perch-cli: moving to {Cm(target)}...", timeout);
                return failure is null
                    ? new ControlResponse(true, _desk.CurrentCm ?? target, target, _desk.DeviceName)
                    : new ControlResponse(false, _desk.CurrentCm, target, _desk.DeviceName, failure, 4);
            }

            default:
                return new ControlResponse(false, Error: $"Unknown command \"{request.Command}\".", Code: 1);
        }
    }

    // ---- plumbing ----------------------------------------------------------

    double ReadTarget()
    {
        var text = _target.Text.Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var cm) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out cm))
            return _settings.LastTarget;

        return Clamp(cm);
    }

    void ShowTarget(double cm)
    {
        cm = Clamp(cm);
        _target.Text = cm.ToString("0.0", CultureInfo.CurrentCulture);
    }

    static string Cm(double cm) => $"{cm.ToString("0.0", CultureInfo.CurrentCulture)} cm";

    void SetDisconnectedUi(string status)
    {
        _tray.Text = "Perch - not connected";
        _subtitle.Text = "Not connected";
        _gauge.Value = null;
        _status.Text = status;
    }

    void SetBusy(bool busy)
    {
        _go.Enabled = !busy;
        _less.Enabled = !busy;
        _more.Enabled = !busy;
        _preset1.Enabled = !busy;
        _preset2.Enabled = !busy;
        _menu.Enabled = !busy;
    }

    static double Clamp(double cm) => Math.Clamp(cm, DeskController.MinCm, DeskController.MaxCm);

    void OnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
