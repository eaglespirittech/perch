using System.Globalization;
using Perch.Ui;

namespace Perch;

/// <summary>
/// One row per weekday: switch it on, set the window it applies to, then the stand slot
/// inside each hour and the two heights. "Copy" clones a finished row onto the others.
/// </summary>
public sealed class ScheduleForm : Form
{
    static readonly DayOfWeek[] DisplayOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    readonly ToggleSwitch[] _enabled = new ToggleSwitch[7];
    readonly Label[] _dayName = new Label[7];
    readonly FieldBox[] _from = new FieldBox[7];
    readonly FieldBox[] _to = new FieldBox[7];
    readonly FieldBox[] _standAt = new FieldBox[7];
    readonly FieldBox[] _standFor = new FieldBox[7];
    readonly FieldBox[] _standCm = new FieldBox[7];
    readonly FieldBox[] _sitCm = new FieldBox[7];
    readonly PerchButton[] _copy = new PerchButton[7];

    readonly Card _grid = new();
    readonly Label _error = new();

    public WeekSchedule Schedule { get; private set; }

    // Column geometry, all relative to the grid card.
    const int ColDay = 16;
    const int ColFrom = 152;
    const int ColUntil = 220;
    const int ColStandAt = 288;
    const int ColStandFor = 354;
    const int ColStandCm = 420;
    const int ColSitCm = 490;
    const int ColCopy = 560;
    const int GridWidth = 636;
    const int HeaderY = 14;
    const int FirstRowY = 44;
    const int RowHeight = 40;

    public ScheduleForm(WeekSchedule schedule)
    {
        Schedule = schedule.Normalized();

        Text = "Schedule";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.Body;
        BackColor = Theme.Window;

        BuildLayout();
        foreach (var day in DisplayOrder) LoadRow(day, Schedule[day]);
    }

    void BuildLayout()
    {
        var title = Caption("Schedule", Theme.Title, Theme.Text, new Point(20, 18));
        var hint = Caption(
            "Inside each active window the desk goes up at that minute past every hour, stays up for " +
            "the given number of minutes, then returns to the other height.",
            Theme.Caption, Theme.TextSecondary, new Point(20, 50));
        hint.AutoSize = false;
        hint.Size = new Size(GridWidth, 42);

        _grid.Bounds = new Rectangle(20, 98, GridWidth, FirstRowY + 7 * RowHeight + 8);
        BuildHeader();
        for (var row = 0; row < DisplayOrder.Length; row++)
            BuildRow(row, DisplayOrder[row]);

        var buttonsY = _grid.Bottom + 16;

        var save = new PerchButton
        {
            Text = "Save",
            Kind = ButtonKind.Primary,
            Bounds = new Rectangle(20 + GridWidth - 200, buttonsY, 96, 36)
        };
        save.Click += OnSave;

        var cancel = new PerchButton
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(20 + GridWidth - 96, buttonsY, 96, 36)
        };

        Caption(_error, string.Empty, Theme.Caption, Theme.Text, new Point(20, buttonsY + 10));

        Controls.AddRange(new Control[] { title, hint, _grid, save, cancel, _error });

        ClientSize = new Size(GridWidth + 40, buttonsY + 36 + 18);
        AcceptButton = save;
        CancelButton = cancel;
    }

    void BuildHeader()
    {
        void Header(string text, int x)
        {
            var label = Caption(text, Theme.Caption, Theme.TextSecondary, new Point(x, HeaderY));
            label.BackColor = Theme.Surface;
            _grid.Controls.Add(label);
        }

        Header("Day", ColDay);
        Header("From", ColFrom);
        Header("Until", ColUntil);
        Header("Stand at", ColStandAt);
        Header("For (min)", ColStandFor);
        Header("Stand cm", ColStandCm);
        Header("Return cm", ColSitCm);
    }

    void BuildRow(int row, DayOfWeek day)
    {
        var i = (int)day;
        var y = FirstRowY + row * RowHeight;

        _enabled[i] = new ToggleSwitch { Bounds = new Rectangle(ColDay, y + 5, 40, 20) };
        _enabled[i].CheckedChanged += (_, _) => UpdateRowEnabled(day);

        _dayName[i] = Caption(day.ToString(), Theme.Body, Theme.Text, new Point(ColDay + 50, y + 7));
        _dayName[i].BackColor = Theme.Surface;

        _from[i] = new FieldBox(58) { Location = new Point(ColFrom, y) };
        _to[i] = new FieldBox(58) { Location = new Point(ColUntil, y) };
        _standAt[i] = new FieldBox(56) { Location = new Point(ColStandAt, y) };
        _standFor[i] = new FieldBox(56) { Location = new Point(ColStandFor, y) };
        _standCm[i] = new FieldBox(60) { Location = new Point(ColStandCm, y) };
        _sitCm[i] = new FieldBox(60) { Location = new Point(ColSitCm, y) };

        _from[i].Input.Leave += (_, _) => NormaliseTime(_from[i]);
        _to[i].Input.Leave += (_, _) => NormaliseTime(_to[i]);
        _standAt[i].Input.Leave += (_, _) => NormaliseWhole(_standAt[i], 0, 59);
        _standFor[i].Input.Leave += (_, _) => NormaliseWhole(_standFor[i], 1, 59);
        _standCm[i].Input.Leave += (_, _) => NormaliseHeight(_standCm[i]);
        _sitCm[i].Input.Leave += (_, _) => NormaliseHeight(_sitCm[i]);

        _copy[i] = new PerchButton
        {
            Text = "Copy",
            Kind = ButtonKind.Subtle,
            Bounds = new Rectangle(ColCopy, y + 2, 60, 30)
        };
        _copy[i].Click += (_, _) => ShowCopyMenu(day, _copy[i]);

        _grid.Controls.AddRange(new Control[]
        {
            _enabled[i], _dayName[i], _from[i], _to[i], _standAt[i], _standFor[i], _standCm[i], _sitCm[i], _copy[i]
        });
    }

    static Label Caption(string text, Font font, Color color, Point at)
    {
        var label = new Label();
        return Caption(label, text, font, color, at);
    }

    static Label Caption(Label label, string text, Font font, Color color, Point at)
    {
        label.Text = text;
        label.Font = font;
        label.ForeColor = color;
        label.Location = at;
        label.AutoSize = true;
        label.BackColor = Color.Transparent;
        return label;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowTrim(this);
    }

    // ---- copy --------------------------------------------------------------

    void ShowCopyMenu(DayOfWeek source, Control anchor)
    {
        var menu = new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ShowImageMargin = false
        };

        void Add(string text, params DayOfWeek[] targets) =>
            menu.Items.Add(text, null, (_, _) => CopyTo(source, targets));

        Add("Copy to all other days", DisplayOrder.Where(d => d != source).ToArray());
        Add("Copy to weekdays (Mon-Fri)", DisplayOrder.Take(5).Where(d => d != source).ToArray());
        Add("Copy to weekend (Sat-Sun)", DisplayOrder.Skip(5).Where(d => d != source).ToArray());
        menu.Items.Add(new ToolStripSeparator());

        foreach (var day in DisplayOrder.Where(d => d != source))
            Add($"Copy to {day}", day);

        menu.Show(anchor, new Point(anchor.Width, anchor.Height), ToolStripDropDownDirection.BelowLeft);
    }

    void CopyTo(DayOfWeek source, DayOfWeek[] targets)
    {
        var values = ReadRow(source);
        foreach (var target in targets)
            LoadRow(target, values.Clone());
    }

    // ---- row values --------------------------------------------------------

    void UpdateRowEnabled(DayOfWeek day)
    {
        var i = (int)day;
        var on = _enabled[i].Checked;

        _dayName[i].ForeColor = on ? Theme.Text : Theme.TextSecondary;
        foreach (var field in new[] { _from[i], _to[i], _standAt[i], _standFor[i], _standCm[i], _sitCm[i] })
            field.SetEnabled(on);
    }

    void LoadRow(DayOfWeek day, DaySchedule value)
    {
        var i = (int)day;
        _enabled[i].SetCheckedSilently(value.Enabled);
        _from[i].Text = Time(value.FromMinute);
        _to[i].Text = Time(value.ToMinute);
        _standAt[i].Text = Math.Clamp(value.StandAtMinute, 0, 59).ToString(CultureInfo.CurrentCulture);
        _standFor[i].Text = Math.Clamp(value.StandMinutes, 1, 59).ToString(CultureInfo.CurrentCulture);
        _standCm[i].Text = Centimetres(value.StandCm);
        _sitCm[i].Text = Centimetres(value.SitCm);
        UpdateRowEnabled(day);
    }

    DaySchedule ReadRow(DayOfWeek day)
    {
        var i = (int)day;
        return new DaySchedule
        {
            Enabled = _enabled[i].Checked,
            FromMinute = ParseTime(_from[i].Text, 9 * 60),
            ToMinute = ParseTime(_to[i].Text, 17 * 60),
            StandAtMinute = ParseWhole(_standAt[i].Text, 50, 0, 59),
            StandMinutes = ParseWhole(_standFor[i].Text, 10, 1, 59),
            StandCm = ParseHeight(_standCm[i].Text, 110),
            SitCm = ParseHeight(_sitCm[i].Text, 72)
        };
    }

    void OnSave(object? sender, EventArgs e)
    {
        var result = new WeekSchedule();

        foreach (var day in DisplayOrder)
        {
            var value = ReadRow(day);
            if (value.Enabled && value.ToMinute <= value.FromMinute)
            {
                _error.Text = $"{day}: the active window has to end after it starts.";
                _error.ForeColor = Theme.Accent;
                _to[(int)day].Input.Focus();
                return;
            }
            result.Days[(int)day] = value;
        }

        Schedule = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---- parsing -----------------------------------------------------------

    void NormaliseTime(FieldBox field) => field.Text = Time(ParseTime(field.Text, 9 * 60));

    static void NormaliseWhole(FieldBox field, int min, int max) =>
        field.Text = ParseWhole(field.Text, min, min, max).ToString(CultureInfo.CurrentCulture);

    static void NormaliseHeight(FieldBox field) => field.Text = Centimetres(ParseHeight(field.Text, DeskController.MinCm));

    static string Time(int minutesSinceMidnight)
    {
        var clamped = Math.Clamp(minutesSinceMidnight, 0, 24 * 60 - 1);
        return $"{clamped / 60:00}:{clamped % 60:00}";
    }

    static string Centimetres(double cm) => cm.ToString("0.0", CultureInfo.CurrentCulture);

    /// <summary>Accepts 9, 9:00, 09:00 and 0900, because people type all four.</summary>
    static int ParseTime(string text, int fallback)
    {
        text = text.Trim();
        if (text.Length == 0) return fallback;

        int hours, minutes;
        var colon = text.IndexOf(':');

        if (colon >= 0)
        {
            if (!int.TryParse(text[..colon], out hours)) return fallback;
            if (!int.TryParse(text[(colon + 1)..], out minutes)) minutes = 0;
        }
        else if (int.TryParse(text, out var digits))
        {
            if (text.Length >= 3) { hours = digits / 100; minutes = digits % 100; }
            else { hours = digits; minutes = 0; }
        }
        else
        {
            return fallback;
        }

        return Math.Clamp(hours, 0, 23) * 60 + Math.Clamp(minutes, 0, 59);
    }

    static int ParseWhole(string text, int fallback, int min, int max) =>
        int.TryParse(text.Trim(), out var value) ? Math.Clamp(value, min, max) : fallback;

    static double ParseHeight(string text, double fallback)
    {
        text = text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var cm) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out cm))
            return fallback;

        return Math.Clamp(cm, DeskController.MinCm, DeskController.MaxCm);
    }
}
