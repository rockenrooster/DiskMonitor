using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>A clickable region inside a painted card: bounds, what it does, and its tooltip and name.</summary>
internal sealed record CardTarget(RectangleF Bounds, Action Run, string Name, string? Tooltip = null, bool Row = true);

/// <summary>
/// A card whose content is painted by its page and whose clickable parts are targets registered while
/// painting. The pointer highlights a row target; arrow keys move between targets and Enter runs one.
/// A card with <see cref="WholeCardAction"/> acts as one button (hover: StrokeStrong; tiles also Hover).
/// </summary>
internal sealed class TargetCard : Card
{
    private readonly List<CardTarget> _targets = [];
    private int _hover = -1;
    private int _focus = -1;

    public TargetCard()
    {
        Backdrop = static t => t.Bg;
    }

    /// <summary>Paints the card's content; call <see cref="AddTarget"/> for each clickable part.</summary>
    public Action<Graphics, TargetCard>? Painter { get; set; }

    /// <summary>When set, the whole card is one button.</summary>
    public Action? WholeCardAction
    {
        get => _whole;
        set
        {
            _whole = value;
            Clickable = value is not null;
        }
    }

    private Action? _whole;

    public int HoverTarget => _hover;

    /// <summary>Where the pointer is over the card, or null; painters use it for row hover.</summary>
    public Point? PointerLocation { get; private set; }

    public int FocusTarget => Focused && KeyboardCues.ShowFocus ? _focus : -1;

    public new float Sf(float value) => base.Sf(value);

    public new int S(int value) => base.S(value);

    public new int Dpi => base.Dpi;

    /// <summary>True while the pointer is over the card (for whole-card hover styles).</summary>
    public bool CardHover => ShowHover;

    public void AddTarget(CardTarget target) => _targets.Add(target);

    public void SetKeyboardTargets(bool enabled)
    {
        TabStop = enabled || _whole is not null;
        SetStyle(ControlStyles.Selectable, TabStop);
    }

    protected override void PaintCard(Graphics g)
    {
        _targets.Clear();
        Painter?.Invoke(g, this);
        if (_whole is null && FocusTarget is var f and >= 0 && f < _targets.Count)
        {
            Draw.FocusRing(g, RectangleF.Inflate(_targets[f].Bounds, -Sf(3), -Sf(3)), Sf(4), Dpi);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (_whole is not null)
        {
            _whole();
            return;
        }

        Point p = PointToClient(MousePosition);
        int index = IndexAt(p);
        if (index >= 0)
        {
            _targets[index].Run();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerLocation = e.Location;
        int index = IndexAt(e.Location);
        if (index != _hover)
        {
            _hover = index;
            ToolTipText = index >= 0 ? _targets[index].Tooltip : null;
            KitTooltip.Hide(this);
            ResetMouseEventArgs();
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        PointerLocation = null;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => _whole is null && keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_whole is null && _targets.Count > 0)
        {
            switch (e.KeyCode)
            {
                case Keys.Up or Keys.Left:
                    _focus = Math.Max(0, _focus - 1);
                    e.Handled = true;
                    Invalidate();
                    return;
                case Keys.Down or Keys.Right:
                    _focus = Math.Min(_targets.Count - 1, _focus + 1);
                    e.Handled = true;
                    Invalidate();
                    return;
                case Keys.Enter or Keys.Space when _focus >= 0 && _focus < _targets.Count:
                    e.Handled = true;
                    _targets[_focus].Run();
                    return;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_focus < 0 && _targets.Count > 0)
        {
            _focus = 0;
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new TargetCardAccessibility(this);

    internal IReadOnlyList<CardTarget> Targets => _targets;

    private int IndexAt(Point p)
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            if (_targets[i].Bounds.Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class TargetCardAccessibility(TargetCard card) : ControlAccessibleObject(card)
    {
        public override int GetChildCount() => card.Targets.Count;

        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < card.Targets.Count ? new TargetAccessibility(card, index, this) : null;
    }

    private sealed class TargetAccessibility(TargetCard card, int index, AccessibleObject parent) : AccessibleObject
    {
        public override string? Name
        {
            get => card.Targets[index].Name;
            set { }
        }

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override AccessibleObject Parent => parent;

        public override Rectangle Bounds => card.RectangleToScreen(Rectangle.Round(card.Targets[index].Bounds));

        public override void DoDefaultAction() => card.Targets[index].Run();
    }
}
