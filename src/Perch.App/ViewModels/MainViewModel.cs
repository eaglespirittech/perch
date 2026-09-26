using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Perch.Desk;
using Perch.Platform;
using Perch.Scheduling;

namespace Perch.App.ViewModels;

/// <summary>
/// Adapts <see cref="DeskSession"/> (the shared, UI-agnostic app logic) to bindable
/// properties and commands. Holds only view state: the text in the "Move to" box.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    string _targetText = string.Empty;
    bool _scheduleEnabled;

    public MainViewModel(DeskSession session, IPlatform platform)
    {
        Session = session;
        Platform = platform;

        GoCommand = new AsyncRelayCommand(() => Session.MoveToAsync(ReadTarget()), () => !IsBusy);
        StopCommand = new AsyncRelayCommand(Session.StopAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        LessCommand = new RelayCommand(() => ShowTarget(ReadTarget() - 0.5), () => !IsBusy);
        MoreCommand = new RelayCommand(() => ShowTarget(ReadTarget() + 0.5), () => !IsBusy);
        PresetCommand = new AsyncRelayCommand<string>(slot => Session.MoveToAsync(Session.Preset(Slot(slot))), _ => !IsBusy);
        SavePresetCommand = new RelayCommand<string>(slot => Session.SavePreset(Slot(slot)));

        _scheduleEnabled = session.Settings.ScheduleEnabled;
        ShowTarget(session.Settings.LastTarget);

        session.StatusChanged += _ => OnPropertyChanged(nameof(Status));
        session.ConnectionChanged += () =>
        {
            OnPropertyChanged(nameof(Connection));
            OnPropertyChanged(nameof(ConnectionText));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(TrayText));
        };
        session.HeightChanged += _ =>
        {
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(TrayText));
        };
        session.BusyChanged += () =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(MoveTarget));
            GoCommand.NotifyCanExecuteChanged();
            LessCommand.NotifyCanExecuteChanged();
            MoreCommand.NotifyCanExecuteChanged();
            PresetCommand.NotifyCanExecuteChanged();
        };
        session.PresetsChanged += () =>
        {
            OnPropertyChanged(nameof(Preset1Text));
            OnPropertyChanged(nameof(Preset2Text));
            OnPropertyChanged(nameof(Note));
        };
        session.ScheduleChanged += () => OnPropertyChanged(nameof(NextMoveText));
        session.TargetShown += ShowTarget;
    }

    public DeskSession Session { get; }
    public IPlatform Platform { get; }

    public IAsyncRelayCommand GoCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IRelayCommand LessCommand { get; }
    public IRelayCommand MoreCommand { get; }
    public IAsyncRelayCommand<string> PresetCommand { get; }
    public IRelayCommand<string> SavePresetCommand { get; }

    public ConnectionState Connection => Session.Connection;
    public string ConnectionText => Session.ConnectionText;

    /// <summary>Null while there is no connection, which the gauge shows as dashes.</summary>
    public double? Height => Session.Desk.IsConnected ? Session.Desk.CurrentCm : null;

    public double? MoveTarget => Session.MoveTarget;
    public string? Note => Session.PresetNote;
    public bool IsBusy => Session.IsBusy;
    public string Status => Session.Status;

    public string TrayText => Height is { } cm ? $"Perch - {DeskSession.Cm(cm)}" : "Perch - not connected";

    public string Preset1Text => DeskSession.Cm(Session.Settings.Preset1);
    public string Preset2Text => DeskSession.Cm(Session.Settings.Preset2);

    public string TargetText
    {
        get => _targetText;
        set => SetProperty(ref _targetText, value);
    }

    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set
        {
            if (!SetProperty(ref _scheduleEnabled, value)) return;
            Session.SetScheduleEnabled(value);
            OnPropertyChanged(nameof(NextMoveText));
        }
    }

    public string NextMoveText => Session.NextMoveText;

    public void SetSchedule(WeekSchedule schedule)
    {
        Session.SetSchedule(schedule);
        OnPropertyChanged(nameof(NextMoveText));
    }

    /// <summary>Tidies whatever was typed once the box loses focus.</summary>
    public void NormaliseTarget() => ShowTarget(ReadTarget());

    /// <summary>Called as the app closes or hides, when the old app used to save.</summary>
    public void Persist()
    {
        Session.Settings.LastTarget = ReadTarget();
        Session.Settings.Save();
    }

    double ReadTarget()
    {
        var text = TargetText.Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var cm) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out cm))
            return Session.Settings.LastTarget;

        return DeskSession.Clamp(cm);
    }

    void ShowTarget(double cm) =>
        TargetText = DeskSession.Clamp(cm).ToString("0.0", CultureInfo.CurrentCulture);

    static int Slot(string? slot) => slot == "2" ? 2 : 1;
}
