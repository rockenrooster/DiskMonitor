using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>An on/off switch. Changes apply immediately; there is no separate "save" step.</summary>
internal sealed class ToggleSwitch : InteractiveSurface
{
    private bool _checked;

    public ToggleSwitch()
    {
        Size = new Size(40, 22);
        Backdrop = palette => palette.Surface;
    }

    /// <summary>Raised when the user flips the switch (not when <see cref="Checked"/> is set from code).</summary>
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
            }
        }
    }

    /// <summary>Standard size at the control's DPI.</summary>
    public ToggleSwitch AutoSized()
    {
        Size = new Size(S(40), S(22));
        return this;
    }

    protected override void OnActivate()
    {
        _checked = !_checked;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        Palette p = P;
        Rectangle track = ClientRectangle;
        int radius = track.Height / 2;

        Color fill;
        Color border;
        Color knob;
        if (!Enabled)
        {
            fill = Backdrop(p);
            border = p.Border;
            knob = p.TextFaint;
        }
        else if (_checked)
        {
            fill = Pressed ? p.AccentPressed : Hovered ? p.AccentHover : p.Accent;
            border = fill;
            knob = p.OnAccent;
        }
        else
        {
            fill = Pressed ? p.SurfacePressed : Hovered ? p.SurfaceHover : p.SurfaceAlt;
            border = Hovered || Pressed ? p.Text : p.BorderStrong;
            knob = Hovered || Pressed ? p.Text : p.TextMuted;
        }

        Draw.FillRounded(graphics, track, radius, fill);
        Draw.StrokeRounded(graphics, track, radius, border);
        if (FocusVisible)
        {
            Draw.StrokeRounded(graphics, track, radius, _checked ? p.Text : p.Accent, S(2));
        }

        int inset = S(4);
        int size = track.Height - inset * 2;
        if (Pressed)
        {
            size += S(2);
        }

        int y = track.Y + (track.Height - size) / 2;
        int x = _checked ? track.Right - inset - size : track.X + inset;
        Draw.FillCircle(graphics, new Rectangle(x, y, size, size), knob);
    }
}
