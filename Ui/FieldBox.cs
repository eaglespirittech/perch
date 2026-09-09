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

    public FieldBox(int width, int height = 30)
    {
        Fill = Theme.Field;
        Radius = Theme.ControlRadius;
        Size = new Size(width, height);

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
    }

    void Centre() => Input.Bounds = new Rectangle(6, Math.Max((Height - Input.Height) / 2, 2), Width - 12, Input.Height);

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
        Input.BackColor = Fill ?? Theme.Field;
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
