using System.Runtime.InteropServices;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>States the render harness can force on a control, so every state can be drawn at once.</summary>
[Flags]
internal enum PreviewState
{
    None = 0,
    Hover = 1,
    Pressed = 2,
    Focus = 4,
    Disabled = 8,
    Selected = 16
}

/// <summary>
/// Base of every custom-painted control: optimised double buffering, all painting in the paint
/// handler, a backdrop the parent sets to the token behind it, DPI helpers, and the harness hook.
/// </summary>
internal class KitControl : Control
{
    private PreviewState _preview;
    private Func<ThemeTokens, Color> _backdrop = static t => t.Bg;

    public KitControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Cursor = Cursors.Default;
        KeyboardCues.EnsureInstalled();
    }

    /// <summary>The token behind the control; rounded shapes are painted over it. A wrong one shows as a halo.</summary>
    public Func<ThemeTokens, Color> Backdrop
    {
        get => _backdrop;
        set
        {
            _backdrop = value;
            Invalidate();
        }
    }

    /// <summary>Forced visual state for the render harness.</summary>
    public PreviewState Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            Invalidate();
        }
    }

    /// <summary>Text shown by the shared kit tooltip when the pointer rests on the control.</summary>
    public string? ToolTipText { get; set; }

    protected ThemeTokens T => ThemeService.Tokens;
    protected int Dpi => ThemeService.Dpi(this);
    protected float Sc => Dpi / 96f;
    protected int S(int value) => ThemeService.S(Dpi, value);
    protected float Sf(float value) => value * Sc;
    protected Font F(FontRole role) => ThemeService.Resources.Font(role, Dpi);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Painted in OnPaint together with the content.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.FillRectangle(ThemeService.Resources.Brush(Backdrop(T)), ClientRectangle);
        Draw.PrepareShapes(g);
        PaintContent(g);
        PaintChildFocusRings(g);
    }

    /// <summary>
    /// Focus rings sit 1 px outside a control, so the parent draws them: children cannot paint outside
    /// their own bounds.
    /// </summary>
    private void PaintChildFocusRings(Graphics g)
    {
        foreach (Control child in Controls)
        {
            if (child is InteractiveKitControl { Visible: true, FocusRingVisible: true } kit)
            {
                Draw.PrepareShapes(g);
                RectangleF ring = kit.FocusRingRect;
                ring.Offset(child.Left, child.Top);
                Draw.FocusRing(g, ring, kit.FocusRingRadius, Dpi);
            }
        }
    }

    /// <summary>Paints the control. The backdrop is already filled and shape settings are applied.</summary>
    protected virtual void PaintContent(Graphics g)
    {
    }

    protected override void OnMouseHover(EventArgs e)
    {
        base.OnMouseHover(e);
        if (!string.IsNullOrEmpty(ToolTipText))
        {
            KitTooltip.Show(this, ToolTipText);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        KitTooltip.Hide(this);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        KitTooltip.Hide(this);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
        Invalidate();
    }
}

/// <summary>
/// A focusable, clickable kit control with hover, pressed, keyboard-focus and disabled states.
/// Space and Enter click it. The focus ring appears only when focus came from the keyboard.
/// </summary>
internal abstract class InteractiveKitControl : KitControl
{
    private bool _hovered;
    private bool _pressed;
    private Form? _hookedForm;

    protected InteractiveKitControl()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        TabStop = true;
    }

    protected bool IsHovered => _hovered;

    protected bool ShowHover => Preview.HasFlag(PreviewState.Hover) || (_hovered && Enabled && !Preview.HasFlag(PreviewState.Disabled));

    protected bool ShowPressed => Preview.HasFlag(PreviewState.Pressed) || (_pressed && _hovered && Enabled);

    protected bool ShowFocus => Preview.HasFlag(PreviewState.Focus) || (Focused && KeyboardCues.ShowFocus && Enabled);

    protected bool ShowDisabled => Preview.HasFlag(PreviewState.Disabled) || !Enabled;

    /// <summary>Whether the parent should draw this control's focus ring now.</summary>
    public virtual bool FocusRingVisible => ShowFocus;

    /// <summary>The shape the ring surrounds, in this control's client coordinates.</summary>
    public virtual RectangleF FocusRingRect => ClientRectangle;

    public virtual float FocusRingRadius => Sf(4);

    private void InvalidateRing()
    {
        Invalidate();
        if (Parent is { } parent)
        {
            Rectangle area = Bounds;
            area.Inflate(S(4), S(4));
            parent.Invalidate(area);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool inside = ClientRectangle.Contains(e.Location);
        if (inside != _hovered)
        {
            _hovered = inside;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _pressed = true;
            if (CanFocus && TabStop)
            {
                Focus();
            }

            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressed)
        {
            _pressed = false;
            Invalidate();
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Enabled && ActivatesOnKeys)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            OnClick(EventArgs.Empty);
        }
    }

    /// <summary>Whether Space and Enter click the control (composite controls handle keys themselves).</summary>
    protected virtual bool ActivatesOnKeys => true;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateRing();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateRing();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _pressed = false;
        InvalidateRing();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HookForm();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        HookForm();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _hookedForm is not null)
        {
            _hookedForm.Deactivate -= OnFormDeactivate;
            _hookedForm = null;
        }

        base.Dispose(disposing);
    }

    // Hover is cleared when the window loses activation, so no stale highlight stays behind.
    private void HookForm()
    {
        Form? form = FindForm();
        if (form == _hookedForm)
        {
            return;
        }

        if (_hookedForm is not null)
        {
            _hookedForm.Deactivate -= OnFormDeactivate;
        }

        _hookedForm = form;
        if (form is not null)
        {
            form.Deactivate += OnFormDeactivate;
        }
    }

    private void OnFormDeactivate(object? sender, EventArgs e)
    {
        if (_hovered || _pressed)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
        }
    }
}

/// <summary>
/// Tracks whether the last input came from the keyboard, so focus rings appear for keyboard users
/// only (a focus ring after a mouse click is noise).
/// </summary>
internal sealed class KeyboardCues : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private static KeyboardCues? _instance;

    public static bool ShowFocus { get; private set; }

    public static void EnsureInstalled()
    {
        if (_instance is null)
        {
            _instance = new KeyboardCues();
            Application.AddMessageFilter(_instance);
        }
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is WmKeyDown or WmSysKeyDown)
        {
            Keys key = (Keys)(int)m.WParam;
            if (key is Keys.Tab or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
            {
                SetShow(true);
            }
        }
        else if (m.Msg is WmLButtonDown or WmRButtonDown)
        {
            SetShow(false);
        }

        return false;
    }

    private static void SetShow(bool value)
    {
        if (ShowFocus == value)
        {
            return;
        }

        ShowFocus = value;
        Control? focused = FromHandle(GetFocus());
        focused?.Invalidate();
        focused?.Parent?.Invalidate();
    }

    private static Control? FromHandle(IntPtr handle) => handle == IntPtr.Zero ? null : Control.FromHandle(handle);

    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern IntPtr GetFocus();
}

/// <summary>
/// One shared UI timer for every animation (progress rings, the indeterminate bar, the drawer, toasts).
/// It runs only while at least one animation is registered.
/// </summary>
internal static class KitAnimator
{
    private static readonly List<Action<long>> Subscribers = [];
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static System.Windows.Forms.Timer? _timer;

    /// <summary>Milliseconds since the app started; animations derive their phase from it.</summary>
    public static long Now => Clock.ElapsedMilliseconds;

    public static void Add(Action<long> tick)
    {
        if (!Subscribers.Contains(tick))
        {
            Subscribers.Add(tick);
        }

        if (_timer is null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (_, _) =>
            {
                long now = Now;
                foreach (Action<long> subscriber in Subscribers.ToArray())
                {
                    subscriber(now);
                }
            };
        }

        _timer.Enabled = true;
    }

    public static void Remove(Action<long> tick)
    {
        Subscribers.Remove(tick);
        if (Subscribers.Count == 0 && _timer is not null)
        {
            _timer.Enabled = false;
        }
    }
}
