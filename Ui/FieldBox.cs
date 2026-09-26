using System.ComponentModel;

namespace Perch.Ui;

/// <summary>
/// A rounded well with a borderless text box in it. Replaces NumericUpDown and
/// DateTimePicker, which draw their own system chrome and stay stubbornly light
/// when Windows is in dark mode.
/// </summary>
public class FieldBox : Card
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox Input { get; }

    public FieldBox(int width, int height = 32)
    {
        Fill = Theme.Field;
        Radius = Theme.ControlRadius;
        Size = new Size((int)this.Dp(width), (int)this.Dp(height));

        Input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Center,
            Font = Theme.Body,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };

        Controls.Add(Input);
        Centre();
        Input.SizeChanged += (_, _) => Centre();
        TrackFocus(this, Input);
    }

    /// <summary>Gives a well an accent outline while the text box inside it has focus.</summary>
    public static void TrackFocus(Card well, TextBox input)
    {
        input.Enter += (_, _) => { well.Stroke = Theme.Accent; well.Invalidate(); };
        input.Leave += (_, _) => { well.Stroke = null; well.Invalidate(); };
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Input is not null) Centre();
    }

    void Centre()
    {
        var inset = (int)this.Dp(6);
        Input.Bounds = new Rectangle(inset, Math.Max((Height - Input.Height) / 2, 2), Width - inset * 2, Input.Height);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => Input.Text;
        set => Input.Text = value ?? string.Empty;
    }

    public void SetEnabled(bool enabled)
    {
        Input.Enabled = enabled;
        Input.ForeColor = enabled ? Theme.Text : Theme.TextDisabled;
        Input.BackColor = SurfaceColor;
        Invalidate();
    }

    public void Retheme()
    {
        Fill = Theme.Field;
        Input.Font = Theme.Body;
        Input.BackColor = Theme.Field;
        Input.ForeColor = Input.Enabled ? Theme.Text : Theme.TextDisabled;
        Centre();
        Invalidate();
    }
}
