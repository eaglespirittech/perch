using System.Drawing.Drawing2D;
using System.Globalization;

namespace Perch.Ui;

/// <summary>
/// The hero readout: current height in large type, over a track showing where that sits
/// in the desk's travel. Reads at a glance from across the room, which is the point.
/// </summary>
public class HeightGauge : Card
{
    double? _value;
    double? _target;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Minimum { get; set; } = DeskController.MinCm;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Maximum { get; set; } = DeskController.MaxCm;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double? Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    /// <summary>Where a move in progress is heading, drawn as a tick on the track.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double? Target
    {
        get => _target;
        set { _target = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var number = _value is { } cm ? cm.ToString("0.0", CultureInfo.InvariantCulture) : "--.-";
        var numberSize = TextRenderer.MeasureText(g, number, Theme.Display, Size.Empty, TextFormatFlags.NoPadding);
        var unitSize = TextRenderer.MeasureText(g, "cm", Theme.Title, Size.Empty, TextFormatFlags.NoPadding);

        var totalWidth = numberSize.Width + 8 + unitSize.Width;
        var left = (Width - totalWidth) / 2;
        var top = 22;

        TextRenderer.DrawText(g, number, Theme.Display, new Point(left, top),
            _value is null ? Theme.TextDisabled : Theme.Text, TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, "cm", Theme.Title,
            new Point(left + numberSize.Width + 8, top + numberSize.Height - unitSize.Height - 10),
            Theme.TextSecondary, TextFormatFlags.NoPadding);

        DrawTrack(g);
    }

    void DrawTrack(Graphics g)
    {
        const int margin = 24;
        const int height = 6;

        var y = Height - 34;
        var width = Width - margin * 2;
        var track = new RectangleF(margin, y, width, height);

        using (var path = Theme.RoundedRect(track, height / 2f))
        using (var brush = new SolidBrush(Theme.Track))
            g.FillPath(brush, path);

        if (_value is { } cm)
        {
            var fraction = (float)Math.Clamp((cm - Minimum) / (Maximum - Minimum), 0, 1);
            if (fraction > 0)
            {
                var filled = new RectangleF(margin, y, Math.Max(width * fraction, height), height);
                using var path = Theme.RoundedRect(filled, height / 2f);
                using var brush = new SolidBrush(Theme.Accent);
                g.FillPath(brush, path);
            }
        }

        if (_target is { } target)
        {
            var fraction = (float)Math.Clamp((target - Minimum) / (Maximum - Minimum), 0, 1);
            var x = margin + width * fraction;
            using var pen = new Pen(Theme.TextSecondary, 2f);
            g.DrawLine(pen, x, y - 5, x, y + height + 5);
        }

        var labelY = y + height + 6;
        TextRenderer.DrawText(g, $"{Minimum:0}", Theme.Caption, new Point(margin - 2, labelY),
            Theme.TextSecondary, TextFormatFlags.NoPadding);

        var maxText = $"{Maximum:0}";
        var maxWidth = TextRenderer.MeasureText(g, maxText, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, maxText, Theme.Caption, new Point(Width - margin - maxWidth + 2, labelY),
            Theme.TextSecondary, TextFormatFlags.NoPadding);
    }
}
