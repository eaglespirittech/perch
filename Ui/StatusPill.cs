using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Perch.Ui;

public enum ConnectionState
{
    Offline,
    Connecting,
    Online
}

/// <summary>A small tinted capsule with a coloured dot: connected, connecting, or not.</summary>
public class StatusPill : Control
{
    ConnectionState _state;

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ConnectionState State
    {
        get => _state;
        set { _state = value; Invalidate(); }
    }

    public void Set(ConnectionState state, string text)
    {
        _state = state;
        Text = text;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitToText();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitToText();
    }

    void FitToText()
    {
        var text = TextRenderer.MeasureText(Text, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding);
        Width = text.Width + (int)this.Dp(10 + 8 + 6 + 12);
    }

    Color Dot => _state switch
    {
        ConnectionState.Online => Theme.Success,
        ConnectionState.Connecting => Theme.Caution,
        _ => Theme.TextSecondary
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Backdrop(this));

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Theme.RoundedRect(bounds, bounds.Height / 2))
        {
            using var fill = new SolidBrush(Theme.Mix(Theme.Backdrop(this), Dot, Theme.IsDark ? 0.14 : 0.10));
            g.FillPath(fill, path);
        }

        var size = this.Dp(8);
        var x = this.Dp(10);
        var y = (Height - size) / 2f;

        if (_state == ConnectionState.Online)
        {
            using var halo = new SolidBrush(Color.FromArgb(60, Dot));
            g.FillEllipse(halo, x - this.Dp(2), y - this.Dp(2), size + this.Dp(4), size + this.Dp(4));
        }

        using (var dot = new SolidBrush(Dot))
            g.FillEllipse(dot, x, y, size, size);

        TextRenderer.DrawText(g, Text, Theme.Caption,
            new Rectangle((int)(x + size + this.Dp(6)), 0, Width, Height), Theme.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
