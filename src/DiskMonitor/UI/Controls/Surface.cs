using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>
/// Base for every custom-painted control: double-buffered, palette-aware, and DPI-aware.
/// Controls read the palette at paint time, so a theme change only needs a repaint.
/// </summary>
internal class Surface : Control
{
    public Surface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);
    }

    /// <summary>
    /// The palette color directly behind this control. Rounded shapes are anti-aliased against
    /// it, so it must match whatever the parent paints there.
    /// </summary>
    public Func<Palette, Color> Backdrop { get; set; } = palette => palette.Window;

    protected static Palette P => Theme.Colors;

    protected int S(int value) => Theme.S(this, value);

    protected Font F(FontRole role) => Theme.Font(this, role);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using SolidBrush brush = new(Backdrop(P));
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }
}

/// <summary>A surface that reacts to the pointer and keyboard: hover, pressed, and focus states.</summary>
internal abstract class InteractiveSurface : Surface
{
    protected InteractiveSurface()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    protected bool Hovered { get; private set; }
    protected bool Pressed { get; private set; }

    /// <summary>True when focus arrived by keyboard and a focus ring should be drawn.</summary>
    protected bool FocusVisible => Focused && ShowFocusCues;

    /// <summary>The control was clicked, or activated with Space or Enter.</summary>
    protected abstract void OnActivate();

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Hovered = false;
        Pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Pressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !Pressed)
        {
            return;
        }

        Pressed = false;
        Invalidate();
        if (ClientRectangle.Contains(e.Location))
        {
            OnActivate();
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter && !e.Handled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            OnActivate();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }
}
