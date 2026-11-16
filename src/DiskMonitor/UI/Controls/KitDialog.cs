using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>A dialog button: its label, variant, and the result it returns.</summary>
internal sealed record DialogButton(string Text, ButtonVariant Variant, int Result, bool InitialFocus = false);

/// <summary>
/// A modal kit dialog over a scrim: Raised, Stroke, radius 8, shadow, centred in the owner window;
/// padding 24, title Subtitle centred 34 px below the top, body from 60 px, blocks 10 px apart, buttons at
/// the bottom right 8 px apart with the primary on the right. Widths 400, 480 or 560. Esc cancels, Enter
/// triggers the focused button, Tab stays inside, and focus returns to the invoker on close.
/// </summary>
internal sealed class KitDialog : Form
{
    private const int BlockGap = 10;
    private readonly string _title;
    private readonly int _logicalWidth;
    private readonly int _cancelResult;
    private readonly List<Block> _blocks = [];
    private readonly List<(DialogButton Spec, KitButton Button)> _buttons = [];
    private Bitmap? _backdrop;
    private Rectangle _surface;

    public KitDialog(string title, int logicalWidth, int cancelResult = -1)
    {
        _title = title;
        _logicalWidth = logicalWidth;
        _cancelResult = cancelResult;
        Result = cancelResult;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = title;
        Text = title;
    }

    public int Result { get; private set; }

    private int Dpi => ThemeService.DpiOverride ?? DeviceDpi;

    private int S(int value) => ThemeService.S(Dpi, value);

    /// <summary>A wrapped paragraph (Body, or Caption for small print).</summary>
    public KitDialog Paragraph(string text, FontRole role = FontRole.Body, bool secondary = false)
    {
        _blocks.Add(new Block(BlockKind.Paragraph, text, null, null, role, secondary));
        return this;
    }

    /// <summary>A fact line: a 16 px icon in Text2 and Body text 28 px further right.</summary>
    public KitDialog Fact(IconName icon, string text)
    {
        _blocks.Add(new Block(BlockKind.Fact, text, icon, null, FontRole.Body, false));
        return this;
    }

    /// <summary>A control in the body, full width, at its logical height.</summary>
    public KitDialog Content(Control control, int logicalHeight)
    {
        if (control is KitControl kit)
        {
            kit.Backdrop = static t => t.Raised;
        }

        _blocks.Add(new Block(BlockKind.Control, "", null, control, FontRole.Body, false) { LogicalHeight = logicalHeight });
        Controls.Add(control);
        return this;
    }

    public KitDialog Button(DialogButton spec)
    {
        KitButton button = new() { Text = spec.Text, Variant = spec.Variant, Backdrop = static t => t.Raised };
        button.Click += (_, _) =>
        {
            Result = spec.Result;
            Close();
        };
        _buttons.Add((spec, button));
        Controls.Add(button);
        return this;
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and returns the chosen result.</summary>
    public int ShowOver(Form owner)
    {
        Control? invoker = owner.ActiveControl;
        Bounds = owner.RectangleToScreen(owner.ClientRectangle);
        _backdrop = Snapshot(owner);
        LayoutDialog();
        _ = ShowDialog(owner);
        if (invoker is { IsDisposed: false, CanFocus: true })
        {
            invoker.Focus();
        }

        return Result;
    }

    /// <summary>Measures and paints the dialog at <paramref name="topLeft"/>, without a window (render harness).</summary>
    public Rectangle PaintInto(Graphics g, Point topLeft)
    {
        _surface = new Rectangle(topLeft, new Size(S(_logicalWidth), MeasureHeight()));
        PaintSurface(g);
        return _surface;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        KitButton? focus = _buttons.FirstOrDefault(b => b.Spec.InitialFocus).Button ?? _buttons.LastOrDefault().Button;
        focus?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            Result = _cancelResult;
            Close();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (_backdrop is not null)
        {
            g.DrawImageUnscaled(_backdrop, 0, 0);
        }
        else
        {
            g.Clear(ThemeService.Tokens.Bg);
        }

        g.FillRectangle(ThemeService.Resources.Brush(ThemeService.Tokens.Scrim), ClientRectangle);
        PaintSurface(g);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _backdrop?.Dispose();
        }

        base.Dispose(disposing);
    }

    private int ContentWidth => S(_logicalWidth) - S(48);

    private int BlockHeight(Block block) => block.Kind switch
    {
        BlockKind.Paragraph => Draw.Wrap(block.Text, ThemeService.Resources.Font(block.Role, Dpi), ContentWidth).Count * S(block.Role == FontRole.Caption ? 16 : 20),
        BlockKind.Fact => S(28),
        _ => S(block.LogicalHeight)
    };

    private int MeasureHeight()
    {
        int y = S(60);
        foreach (Block block in _blocks)
        {
            y += BlockHeight(block) + S(BlockGap);
        }

        if (_blocks.Count > 0)
        {
            y -= S(BlockGap);
        }

        return y + S(32) + S(32) + S(24);
    }

    private void LayoutDialog()
    {
        int width = S(_logicalWidth);
        int height = MeasureHeight();
        _surface = new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        int y = _surface.Y + S(60);
        foreach (Block block in _blocks)
        {
            int blockHeight = BlockHeight(block);
            block.Control?.SetBounds(_surface.X + S(24), y, ContentWidth, blockHeight);
            y += blockHeight + S(BlockGap);
        }

        int x = _surface.Right - S(24);
        for (int i = _buttons.Count - 1; i >= 0; i--)
        {
            KitButton button = _buttons[i].Button;
            button.SizeToContent();
            x -= button.Width;
            button.Location = new Point(x, _surface.Bottom - S(24) - button.Height);
            x -= S(8);
        }
    }

    private void PaintSurface(Graphics g)
    {
        ThemeTokens t = ThemeService.Tokens;
        Draw.PrepareShapes(g);
        Surfaces.DialogSurface(g, _surface, _title, Dpi);
        int y = _surface.Y + S(60);
        int x = _surface.X + S(24);
        foreach (Block block in _blocks)
        {
            int height = BlockHeight(block);
            switch (block.Kind)
            {
                case BlockKind.Paragraph:
                {
                    Font font = ThemeService.Resources.Font(block.Role, Dpi);
                    int lineHeight = S(block.Role == FontRole.Caption ? 16 : 20);
                    List<string> lines = Draw.Wrap(block.Text, font, ContentWidth);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Draw.Text(g, lines[i], font, x, y + i * lineHeight + lineHeight / 2f, block.Secondary ? t.Text3 : t.Text);
                    }

                    break;
                }

                case BlockKind.Fact:
                    Draw.Icon(g, block.Icon!.Value, x, y + S(6), 16, Dpi, t.Text2);
                    Draw.Text(g, block.Text, ThemeService.Resources.Font(FontRole.Body, Dpi), x + S(28), y + S(14), t.Text, ContentWidth - S(28));
                    break;
            }

            y += height + S(BlockGap);
        }
    }

    private static Bitmap Snapshot(Form owner)
    {
        // The client area only: each top-level child is captured where it sits.
        Bitmap bitmap = new(Math.Max(1, owner.ClientSize.Width), Math.Max(1, owner.ClientSize.Height));
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(ThemeService.Tokens.Bg);
        foreach (Control child in owner.Controls)
        {
            if (!child.Visible || child.Width <= 0 || child.Height <= 0)
            {
                continue;
            }

            try
            {
                using Bitmap part = new(child.Width, child.Height);
                child.DrawToBitmap(part, new Rectangle(Point.Empty, child.Size));
                g.DrawImageUnscaled(part, child.Location);
            }
            catch (ArgumentException)
            {
                // A control that cannot be captured leaves the plain background, which is fine behind a scrim.
            }
        }

        return bitmap;
    }

    private enum BlockKind
    {
        Paragraph,
        Fact,
        Control
    }

    private sealed record Block(BlockKind Kind, string Text, IconName? Icon, Control? Control, FontRole Role, bool Secondary)
    {
        public int LogicalHeight { get; init; }
    }
}
