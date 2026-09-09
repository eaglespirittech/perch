namespace Perch;

/// <summary>
/// One row per weekday: switch it on, set the window it applies to, then the stand slot
/// inside each hour and the two heights. "Copy to" clones a finished row onto the others.
/// </summary>
public sealed class ScheduleForm : Form
{
    static readonly DayOfWeek[] DisplayOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    readonly CheckBox[] _enabled = new CheckBox[7];
    readonly DateTimePicker[] _from = new DateTimePicker[7];
    readonly DateTimePicker[] _to = new DateTimePicker[7];
    readonly NumericUpDown[] _standAt = new NumericUpDown[7];
    readonly NumericUpDown[] _standFor = new NumericUpDown[7];
    readonly NumericUpDown[] _standCm = new NumericUpDown[7];
    readonly NumericUpDown[] _sitCm = new NumericUpDown[7];

    public WeekSchedule Schedule { get; private set; }

    public ScheduleForm(WeekSchedule schedule)
    {
        Schedule = schedule.Normalized();

        Text = "Schedule";
        ClientSize = new Size(676, 362);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        BuildHeader();
        for (var row = 0; row < DisplayOrder.Length; row++)
            BuildRow(row, DisplayOrder[row]);
        BuildFooter();

        foreach (var day in DisplayOrder) LoadRow(day, Schedule[day]);
    }

    const int ColDay = 12;
    const int ColFrom = 124;
    const int ColTo = 206;
    const int ColStandAt = 298;
    const int ColStandFor = 368;
    const int ColStandCm = 442;
    const int ColSitCm = 518;
    const int ColCopy = 596;
    const int FirstRowY = 50;
    const int RowHeight = 32;

    void BuildHeader()
    {
        void Header(string text, int x, int width) => Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, 16),
            MinimumSize = new Size(width, 20),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        });

        Header("Day", ColDay, 100);
        Header("From", ColFrom, 74);
        Header("Until", ColTo, 74);
        Header("Stand at", ColStandAt, 66);
        Header("For (min)", ColStandFor, 70);
        Header("Stand cm", ColStandCm, 74);
        Header("Return cm", ColSitCm, 74);
    }

    void BuildRow(int row, DayOfWeek day)
    {
        var i = (int)day;
        var y = FirstRowY + row * RowHeight;

        _enabled[i] = new CheckBox
        {
            Text = day.ToString(),
            Location = new Point(ColDay, y + 4),
            AutoSize = true
        };
        _enabled[i].CheckedChanged += (_, _) => UpdateRowEnabled(day);

        _from[i] = TimeBox(ColFrom, y);
        _to[i] = TimeBox(ColTo, y);

        _standAt[i] = Spin(ColStandAt, y, 0, 59, 0);
        _standFor[i] = Spin(ColStandFor, y, 1, 59, 0);
        _standCm[i] = Spin(ColStandCm, y, (decimal)DeskController.MinCm, (decimal)DeskController.MaxCm, 1);
        _sitCm[i] = Spin(ColSitCm, y, (decimal)DeskController.MinCm, (decimal)DeskController.MaxCm, 1);

        var copy = new Button
        {
            Text = "Copy to",
            Location = new Point(ColCopy, y),
            MinimumSize = new Size(68, 25),
            AutoSize = true
        };
        copy.Click += (_, _) => ShowCopyMenu(day, copy);

        Controls.AddRange(new Control[]
        {
            _enabled[i], _from[i], _to[i], _standAt[i], _standFor[i], _standCm[i], _sitCm[i], copy
        });
    }

    static DateTimePicker TimeBox(int x, int y) => new()
    {
        Location = new Point(x, y),
        Size = new Size(74, 23),
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true
    };

    static NumericUpDown Spin(int x, int y, decimal min, decimal max, int decimals) => new()
    {
        Location = new Point(x, y),
        Size = new Size(66, 23),
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Increment = decimals > 0 ? 0.5M : 5M
    };

    void BuildFooter()
    {
        Controls.Add(new Label
        {
            Text = "Inside the active window the desk goes up at that minute past every hour, stays up " +
                   "for the given number of minutes, then returns to the other height.",
            Location = new Point(ColDay, FirstRowY + 7 * RowHeight + 8),
            Size = new Size(652, 40),
            ForeColor = SystemColors.GrayText
        });

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.None,
            Location = new Point(484, 320),
            Size = new Size(86, 30)
        };
        ok.Click += OnOk;

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(578, 320),
            Size = new Size(86, 30)
        };

        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    void ShowCopyMenu(DayOfWeek source, Control anchor)
    {
        var menu = new ContextMenuStrip();

        void Add(string text, params DayOfWeek[] targets) =>
            menu.Items.Add(text, null, (_, _) => CopyTo(source, targets));

        Add("All other days", DisplayOrder.Where(d => d != source).ToArray());
        Add("Weekdays (Mon-Fri)", DisplayOrder.Take(5).Where(d => d != source).ToArray());
        Add("Weekend (Sat-Sun)", DisplayOrder.Skip(5).Where(d => d != source).ToArray());
        menu.Items.Add(new ToolStripSeparator());

        foreach (var day in DisplayOrder.Where(d => d != source))
            Add(day.ToString(), day);

        menu.Show(anchor, new Point(0, anchor.Height));
    }

    void CopyTo(DayOfWeek source, DayOfWeek[] targets)
    {
        var values = ReadRow(source);
        foreach (var target in targets)
        {
            var copy = values.Clone();
            LoadRow(target, copy);
        }
    }

    void UpdateRowEnabled(DayOfWeek day)
    {
        var i = (int)day;
        var on = _enabled[i].Checked;
        _from[i].Enabled = on;
        _to[i].Enabled = on;
        _standAt[i].Enabled = on;
        _standFor[i].Enabled = on;
        _standCm[i].Enabled = on;
        _sitCm[i].Enabled = on;
    }

    void LoadRow(DayOfWeek day, DaySchedule value)
    {
        var i = (int)day;
        _enabled[i].Checked = value.Enabled;
        _from[i].Value = Today(value.FromMinute);
        _to[i].Value = Today(value.ToMinute);
        _standAt[i].Value = Math.Clamp(value.StandAtMinute, 0, 59);
        _standFor[i].Value = Math.Clamp(value.StandMinutes, 1, 59);
        _standCm[i].Value = ClampCm(value.StandCm);
        _sitCm[i].Value = ClampCm(value.SitCm);
        UpdateRowEnabled(day);
    }

    DaySchedule ReadRow(DayOfWeek day)
    {
        var i = (int)day;
        return new DaySchedule
        {
            Enabled = _enabled[i].Checked,
            FromMinute = _from[i].Value.Hour * 60 + _from[i].Value.Minute,
            ToMinute = _to[i].Value.Hour * 60 + _to[i].Value.Minute,
            StandAtMinute = (int)_standAt[i].Value,
            StandMinutes = (int)_standFor[i].Value,
            StandCm = (double)_standCm[i].Value,
            SitCm = (double)_sitCm[i].Value
        };
    }

    void OnOk(object? sender, EventArgs e)
    {
        var result = new WeekSchedule();
        foreach (var day in DisplayOrder)
        {
            var value = ReadRow(day);
            if (value.Enabled && value.ToMinute <= value.FromMinute)
            {
                MessageBox.Show(this,
                    $"{day}: the active window has to end after it starts.",
                    "Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _to[(int)day].Focus();
                return;
            }
            result.Days[(int)day] = value;
        }

        Schedule = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    static DateTime Today(int minutesSinceMidnight) =>
        DateTime.Today.AddMinutes(Math.Clamp(minutesSinceMidnight, 0, 24 * 60 - 1));

    static decimal ClampCm(double cm) =>
        Math.Clamp((decimal)cm, (decimal)DeskController.MinCm, (decimal)DeskController.MaxCm);
}
