using System.Drawing.Drawing2D;

namespace Perch.Ui;

/// <summary>A Fluent-style on/off switch, with a short slide when it flips.</summary>
public class ToggleSwitch : Control
{
    readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    float _position; // 0 = off, 1 = on
    bool _checked;
    bool _hovered;

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(44, 22);
        TabStop = true;
        Cursor = Cursors.Hand;

        _animation.Tick += (_, _) =>
        {
            var target = _checked ? 1f : 0f;
            var step = 0.18f;

            if (Math.Abs(_position - target) <= step)
            {
                _position = target;
                _animation.Stop();
            }
            else
            {
                _position += _position < target ? step : -step;
            }

            Invalidate();
        };
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _animation.Start();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets the state without animating or raising the event, for initial load.</summary>
    public void SetCheckedSilently(bool value)
    {
        _checked = value;
        _position = value ? 1f : 0f;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        var track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(track, Height / 2f);

        var on = _position;
        var fill = Blend(Theme.Field, _hovered ? Theme.AccentHover : Theme.Accent, on);
        var border = Blend(Theme.IsDark ? Theme.Shift(Theme.Border, 0.25) : Theme.TextSecondary, Theme.Accent, on);

        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        using (var pen = new Pen(border))
            g.DrawPath(pen, path);

        var knobSize = Height - 8;
        var travel = Width - knobSize - 8;
        var knob = new RectangleF(4 + travel * on, 4, knobSize, knobSize);
        using (var brush = new SolidBrush(Blend(Theme.IsDark ? Theme.Text : Theme.TextSecondary, Theme.OnAccent, on)))
            g.FillEllipse(brush, knob);

        if (Focused && ShowFocusCues)
        {
            var focus = RectangleF.Inflate(track, 1.5f, 1.5f);
            using var focusPath = Theme.RoundedRect(focus, focus.Height / 2f);
            using var pen = new Pen(Theme.Text, 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, focusPath);
        }
    }

    static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
