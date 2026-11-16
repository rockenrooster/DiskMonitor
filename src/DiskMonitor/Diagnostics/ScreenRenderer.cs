using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using DiskMonitor.Core;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Diagnostics;

/// <summary>
/// <c>--render-screens &lt;dir&gt;</c>: renders every screen and gallery with invented data to PNG, in dark,
/// light and high contrast at 100 % and 150 %, and writes index.html with each render beside its reference.
/// Nothing reaches the user's screen and nothing is written outside the output folder.
/// </summary>
internal static class ScreenRenderer
{
    public const string Switch = "--render-screens";

    /// <summary>Screens contributed by the shell and pages (milestones M3 and M4).</summary>
    public static readonly List<(string Name, Size LogicalSize, Func<int, Bitmap> Render)> Screens = [];

    public static int Run(string[] args)
    {
        // First, before anything touches AppPaths: point the data folder at a fresh temporary one.
        string dataDir = Path.Combine(Path.GetTempPath(), "DiskMonitor-render-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DISKMONITOR_DATA_DIR", dataDir);
        string output = Path.GetFullPath(Core.Chkdsk.CommandLine.Value(args, Switch) ?? "screens");
        string? only = Core.Chkdsk.CommandLine.Value(args, "--only");
        Directory.CreateDirectory(output);
        ApplicationConfiguration.Initialize();
        Stopwatch timer = Stopwatch.StartNew();
        Clock.Pin(DemoNow.Utc);
        List<(string Screen, string Theme, int Scale, string File)> renders = [];
        try
        {
            RegisterScreens();
            foreach ((string themeName, Func<ThemeTokens> tokens) in Themes())
            {
                ThemeService.Set(tokens());
                foreach (int scale in new[] { 100, 150 })
                {
                    int dpi = 96 * scale / 100;
                    ThemeService.DpiOverride = dpi;
                    foreach ((string name, Size _, Func<int, Bitmap> render) in Screens)
                    {
                        if (only is not null && !name.StartsWith(only, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string file = $"{name}-{themeName}-{scale}.png";
                        using Bitmap bitmap = render(dpi);
                        bitmap.Save(Path.Combine(output, file), ImageFormat.Png);
                        renders.Add((name, themeName, scale, file));
                    }
                }
            }

            WriteIndex(output, renders);
            Console.Out.WriteLine($"Rendered {renders.Count} images to {output} in {timer.Elapsed.TotalSeconds:0.0} s");
            return 0;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Render screens", ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            ThemeService.DpiOverride = null;
            Clock.Pin(null);
            try
            {
                if (Directory.Exists(dataDir))
                {
                    Directory.Delete(dataDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // A locked temporary file is removed by Windows later.
            }
        }
    }

    private static IEnumerable<(string Name, Func<ThemeTokens> Tokens)> Themes()
    {
        yield return ("dark", () => ThemeTokens.Dark);
        yield return ("light", () => ThemeTokens.Light);
        yield return ("contrast", HighContrastSample);
    }

    /// <summary>A fixed contrast theme (Windows "Night sky" colours) so renders do not depend on the machine.</summary>
    private static ThemeTokens HighContrastSample() => ThemeTokens.HighContrast() with
    {
        Bg = Color.Black,
        Surface = Color.Black,
        Raised = Color.Black,
        Hover = Color.Black,
        Pressed = Color.Black,
        NeutralPill = Color.Black,
        CritPill = Color.Black,
        HighPill = Color.Black,
        MedPill = Color.Black,
        LowPill = Color.Black,
        OkPill = Color.Black,
        Text = Color.White,
        Text2 = Color.White,
        Text3 = Color.White,
        Stroke = Color.White,
        StrokeStrong = Color.White,
        CritText = Color.White,
        CritFill = Color.White,
        HighText = Color.White,
        HighFill = Color.White,
        MedText = Color.White,
        MedFill = Color.White,
        LowText = Color.White,
        LowFill = Color.White,
        OkText = Color.White,
        OkFill = Color.White,
        TextDisabled = Color.FromArgb(0xA6, 0xA6, 0xA6),
        Accent = Color.FromArgb(0xD6, 0xB4, 0xFD),
        AccentHover = Color.FromArgb(0xD6, 0xB4, 0xFD),
        AccentPressed = Color.FromArgb(0xD6, 0xB4, 0xFD),
        Selected = Color.FromArgb(0xD6, 0xB4, 0xFD),
        OnAccent = Color.Black,
        OnSelected = Color.Black,
        DangerFill = Color.FromArgb(0xD6, 0xB4, 0xFD),
        DangerHover = Color.FromArgb(0xD6, 0xB4, 0xFD),
        DangerPressed = Color.FromArgb(0xD6, 0xB4, 0xFD),
        OnDanger = Color.Black,
        IsDark = true
    };

    private static bool _registered;

    private static void RegisterScreens()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        Screens.Insert(0, ("components", new Size(1280, 1434), dpi => ReferenceSheets.Components(dpi)));
        Screens.Insert(1, ("dialogs", new Size(1280, 1396), dpi => ReferenceSheets.Dialogs(dpi)));
        Screens.Insert(2, ("icons", new Size(1280, 852), dpi => ReferenceSheets.Icons(dpi)));
        Screens.Insert(3, ("gallery-disk-monitor", new Size(1280, 900), dpi => ReferenceSheets.Gallery(dpi)));
    }

    /// <summary>Paints a canvas of child controls and painted items to a bitmap, off-screen.</summary>
    public static Bitmap Capture(Control root, int width, int height)
    {
        using Form host = new()
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            AutoScaleMode = AutoScaleMode.None,
            ClientSize = new Size(width, height)
        };
        root.Bounds = new Rectangle(0, 0, width, height);
        host.Controls.Add(root);
        host.Show();
        Application.DoEvents();
        root.PerformLayout();
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        root.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        host.Hide();
        host.Controls.Remove(root);
        root.Dispose();
        return bitmap;
    }

    private static void WriteIndex(string output, List<(string Screen, string Theme, int Scale, string File)> renders)
    {
        string references = FindReferences();
        StringBuilder html = new();
        html.Append("<!doctype html><meta charset=\"utf-8\"><title>Disk Monitor renders</title>");
        html.Append("<style>body{background:#1f1f1f;color:#f5f5f5;font-family:Segoe UI,sans-serif;margin:24px}");
        html.Append("h2{font-size:16px;margin:32px 0 8px}figure{display:inline-block;margin:0 16px 16px 0;vertical-align:top}");
        html.Append("figcaption{font-size:12px;color:#cecece;margin-bottom:4px}img{border:1px solid #454545;max-width:100%}</style>");
        html.Append("<h1>Disk Monitor renders</h1>");
        foreach (IGrouping<string, (string Screen, string Theme, int Scale, string File)> group in renders.GroupBy(r => r.Screen))
        {
            html.Append("<h2>").Append(group.Key).Append("</h2>");
            foreach ((string _, string theme, int scale, string file) in group)
            {
                html.Append("<figure><figcaption>").Append(theme).Append(" · ").Append(scale).Append(" %</figcaption>");
                html.Append("<img src=\"").Append(file).Append("\"></figure>");
                string reference = Path.Combine(references, $"{group.Key}-{theme}.png");
                if (scale == 100 && File.Exists(reference))
                {
                    html.Append("<figure><figcaption>reference · ").Append(theme).Append("</figcaption>");
                    html.Append("<img src=\"").Append(new Uri(reference).AbsoluteUri).Append("\"></figure>");
                }
            }
        }

        File.WriteAllText(Path.Combine(output, "index.html"), html.ToString(), new UTF8Encoding(false));
    }

    // The reference PNGs live in the repository's docs/plans/design/mediaverifier/png folder.
    private static string FindReferences()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "docs", "plans", "design", "mediaverifier", "png");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return "";
    }
}

/// <summary>The fixed "now" of demo mode and the harness: Monday 16 November 2026, 14:20 local time.</summary>
internal static class DemoNow
{
    public static readonly DateTime Local = new(2026, 11, 16, 14, 20, 0, DateTimeKind.Local);

    public static DateTime Utc => Local.ToUniversalTime();
}

/// <summary>A canvas for galleries: painted items plus real controls, at logical coordinates scaled for a DPI.</summary>
internal sealed class SheetCanvas : KitControl
{
    private readonly List<Action<Graphics, float>> _painters = [];
    private readonly List<(Control Control, RectangleF Logical)> _children = [];

    public SheetCanvas(int dpi)
    {
        CanvasDpi = dpi;
    }

    public int CanvasDpi { get; }

    public float ScaleFactor => CanvasDpi / 96f;

    /// <summary>Adds a painter; it receives the scale factor (device pixels per logical pixel).</summary>
    public void Draw(Action<Graphics, float> painter) => _painters.Add(painter);

    /// <summary>Places a control at logical coordinates.</summary>
    public T Add<T>(T control, float x, float y, float width, float height) where T : Control
    {
        _children.Add((control, new RectangleF(x, y, width, height)));
        Controls.Add(control);
        return control;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        float s = ScaleFactor;
        foreach ((Control control, RectangleF r) in _children)
        {
            control.SetBounds((int)Math.Round(r.X * s), (int)Math.Round(r.Y * s), (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
        }
    }

    protected override void PaintContent(Graphics g)
    {
        foreach (Action<Graphics, float> painter in _painters)
        {
            UI.Theming.Draw.PrepareShapes(g);
            painter(g, ScaleFactor);
        }
    }
}
