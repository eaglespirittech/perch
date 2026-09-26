using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Perch.Ui;

/// <summary>
/// A preset as a large target: a numbered badge, its name, and the height it moves to.
/// Still a Button underneath, so Tab, Enter and screen readers treat it as one.
/// </summary>
public class PresetTile : PerchButton
{
    public PresetTile()
    {
        Kind = ButtonKind.Subtle;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Badge { get; set; } = "1";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value { get; set; } = string.Empty;

    /// <summary>Updates what the tile shows, and what it announces.</summary>
    public void Show(string label, string value)
    {
        Label = label;
        Value = value;
        Text = $"{label}, {value}";
        Invalidate();
    }

    protected override void PaintContent(Graphics g, Color text)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var badgeSize = this.Dp(36);
        var left = this.Dp(12);
        var badge = new RectangleF(left, (Height - badgeSize) / 2f, badgeSize, badgeSize);

        using (var path = Theme.RoundedRect(badge, this.Dp(10)))
        using (var brush = new SolidBrush(Enabled ? Theme.AccentSoft : Theme.Field))
            g.FillPath(brush, path);

        TextRenderer.DrawText(g, Badge, Theme.BodyStrong, Rectangle.Round(badge),
            Enabled ? Theme.Accent : Theme.TextDisabled,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var textLeft = (int)(badge.Right + this.Dp(12));
        var labelSize = TextRenderer.MeasureText(g, Label, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding);
        var valueSize = TextRenderer.MeasureText(g, Value, Theme.ValueLarge, Size.Empty, TextFormatFlags.NoPadding);
        var gap = (int)this.Dp(1);
        var top = (Height - labelSize.Height - gap - valueSize.Height) / 2;

        TextRenderer.DrawText(g, Label, Theme.Caption, new Point(textLeft, top),
            Enabled ? Theme.TextSecondary : Theme.TextDisabled, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Value, Theme.ValueLarge, new Point(textLeft, top + labelSize.Height + gap),
            text, TextFormatFlags.NoPadding);

        // A chevron hints that the tile does something when pressed.
        TextRenderer.DrawText(g, "", Theme.IconSmall,
            new Rectangle(0, 0, Width - (int)this.Dp(12), Height), Enabled ? Theme.TextSecondary : Theme.TextDisabled,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
