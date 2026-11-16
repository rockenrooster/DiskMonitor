using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Controls;

/// <summary>
/// A top-level window drawn with per-pixel alpha, so a floating surface (menu, drop-down list, tooltip,
/// toast) can carry the kit shadow outside its rounded rectangle. The surface is rendered opaque (so
/// ClearType text works) and composited with anti-aliased corners over its shadow.
/// </summary>
internal abstract class LayeredPopup : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int UlwAlpha = 2;
    private const byte AcSrcAlpha = 1;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private byte _opacity = 255;

    protected LayeredPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
    }

    /// <summary>The DPI the popup is drawn at (the owner's).</summary>
    public int PopupDpi { get; set; } = 96;

    /// <summary>Whether the popup takes keyboard focus (menus) or never activates (tooltips, toasts).</summary>
    protected virtual bool Activates => false;

    protected virtual bool TopMostPopup => false;

    /// <summary>Device-pixel size of the surface itself, without its shadow.</summary>
    protected abstract Size SurfaceSize { get; }

    protected virtual int CornerRadius => ThemeService.S(PopupDpi, 8);

    protected virtual Color BorderColor => ThemeService.Tokens.Stroke;

    protected int ShadowMargin => (int)Math.Ceiling(ThemeService.Sf(PopupDpi, 30));

    protected override bool ShowWithoutActivation => !Activates;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style = WsPopup;
            cp.ExStyle |= WsExLayered | WsExToolWindow;
            if (!Activates)
            {
                cp.ExStyle |= WsExNoActivate;
            }

            if (TopMostPopup)
            {
                cp.ExStyle |= WsExTopmost;
            }

            return cp;
        }
    }

    /// <summary>Screen position of the surface's top-left corner (the window is larger by the shadow margin).</summary>
    public Point SurfaceLocation
    {
        get => new(Left + ShadowMargin, Top + ShadowMargin);
        set => Location = new Point(value.X - ShadowMargin, value.Y - ShadowMargin);
    }

    /// <summary>Converts a screen point into surface coordinates.</summary>
    protected Point ToSurface(Point client) => new(client.X - ShadowMargin, client.Y - ShadowMargin);

    public byte PopupOpacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            if (IsHandleCreated)
            {
                Render();
            }
        }
    }

    /// <summary>Paints the surface into an opaque bitmap whose origin is the surface's top-left corner.</summary>
    protected abstract void PaintSurface(Graphics g, Rectangle bounds);

    /// <summary>Redraws the window from <see cref="PaintSurface"/>.</summary>
    public void Render()
    {
        Size surface = SurfaceSize;
        if (surface.Width <= 0 || surface.Height <= 0)
        {
            return;
        }

        int margin = ShadowMargin;
        Size full = new(surface.Width + 2 * margin, surface.Height + 2 * margin);
        if (Size != full)
        {
            Size = full;
        }

        using Bitmap content = new(surface.Width, surface.Height, PixelFormat.Format32bppRgb);
        using (Graphics g = Graphics.FromImage(content))
        {
            g.Clear(ThemeService.Tokens.Raised);
            Draw.PrepareShapes(g);
            PaintSurface(g, new Rectangle(Point.Empty, surface));
        }

        using Bitmap layered = new(full.Width, full.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(layered))
        {
            g.Clear(Color.Transparent);
            Draw.Shadow(g, new Rectangle(margin, margin, surface.Width, surface.Height), CornerRadius, PopupDpi);
            Draw.PrepareShapes(g);
            using TextureBrush texture = new(content, WrapMode.Clamp);
            texture.TranslateTransform(margin, margin);
            using GraphicsPath shape = new();
            IconGeometry.AddRoundedRect(shape, margin, margin, surface.Width, surface.Height, CornerRadius);
            g.FillPath(texture, shape);
            Draw.StrokeRounded(g, new RectangleF(margin, margin, surface.Width, surface.Height), CornerRadius, BorderColor, ThemeService.Sf(PopupDpi, 1));
        }

        UpdateLayered(layered);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseActivate && !Activates)
        {
            m.Result = MaNoActivate;
            return;
        }

        base.WndProc(ref m);
    }

    private void UpdateLayered(Bitmap bitmap)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr previous = SelectObject(memory, hBitmap);
        try
        {
            NativeSize size = new(bitmap.Width, bitmap.Height);
            NativePoint source = new(0, 0);
            NativePoint top = new(Left, Top);
            BlendFunction blend = new() { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = _opacity, AlphaFormat = AcSrcAlpha };
            _ = UpdateLayeredWindow(Handle, screen, ref top, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(hBitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int cx, int cy)
    {
        public int Cx = cx;
        public int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref NativePoint position, ref NativeSize size, IntPtr source, ref NativePoint sourcePosition, int key, ref BlendFunction blend, int flags);

    [DllImport("user32.dll", EntryPoint = "GetDC")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    private static extern bool DeleteObject(IntPtr obj);
}

/// <summary>The shared kit tooltip: 26 px high, radius 4, Raised, Stroke border, shadow, Caption text.</summary>
internal sealed class KitTooltip : LayeredPopup
{
    private static KitTooltip? _instance;
    private static Control? _owner;
    private string _text = "";

    private KitTooltip()
    {
    }

    protected override bool TopMostPopup => true;

    protected override int CornerRadius => ThemeService.S(PopupDpi, 4);

    protected override Size SurfaceSize => Measure(_text, PopupDpi);

    public static Size Measure(string text, int dpi) =>
        new(Draw.TextWidth(text, ThemeService.Resources.Font(FontRole.Caption, dpi)) + ThemeService.S(dpi, 16), ThemeService.S(dpi, 26));

    /// <summary>Paints a tooltip surface at <paramref name="bounds"/>; used by the popup and the harness.</summary>
    public static void PaintTooltip(Graphics g, Rectangle bounds, string text, int dpi)
    {
        ThemeTokens t = ThemeService.Tokens;
        Draw.Text(g, text, ThemeService.Resources.Font(FontRole.Caption, dpi), bounds.X + ThemeService.S(dpi, 8), bounds.Y + bounds.Height / 2f, t.Text);
    }

    protected override void PaintSurface(Graphics g, Rectangle bounds) => PaintTooltip(g, bounds, _text, PopupDpi);

    /// <summary>Shows <paramref name="text"/> 8 px below the pointer, over <paramref name="owner"/>.</summary>
    public static void Show(Control owner, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !owner.IsHandleCreated || owner.FindForm() is not { } form || !form.Visible)
        {
            return;
        }

        _instance ??= new KitTooltip();
        _owner = owner;
        _instance._text = text;
        _instance.PopupDpi = ThemeService.Dpi(owner);
        Point cursor = Cursor.Position;
        Size size = _instance.SurfaceSize;
        Rectangle screen = Screen.FromPoint(cursor).WorkingArea;
        int x = Math.Clamp(cursor.X, screen.Left, Math.Max(screen.Left, screen.Right - size.Width));
        // 8 px below the pointer: the arrow cursor is about 16 px tall below its hot spot.
        int y = cursor.Y + ThemeService.S(_instance.PopupDpi, 8 + 16);
        if (y + size.Height > screen.Bottom)
        {
            y = cursor.Y - size.Height - ThemeService.S(_instance.PopupDpi, 8);
        }

        _instance.SurfaceLocation = new Point(x, y);
        if (!_instance.Visible)
        {
            _instance.Show();
        }

        _instance.Render();
    }

    public static void Hide(Control owner)
    {
        if (_instance is { Visible: true } && (_owner == owner || _owner is null))
        {
            _instance.Visible = false;
            _owner = null;
        }
    }
}
