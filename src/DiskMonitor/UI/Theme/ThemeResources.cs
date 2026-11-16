using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DiskMonitor.UI.Theming;

/// <summary>
/// Brushes, pens, fonts, icon paths, rounded-rectangle paths and shadow bitmaps, cached per theme and
/// DPI so painting allocates nothing. Disposed as a whole when the theme changes.
/// </summary>
internal sealed class ThemeResources : IDisposable
{
    private readonly Dictionary<int, SolidBrush> _brushes = [];
    private readonly Dictionary<(int Argb, float Width, bool Round), Pen> _pens = [];
    private readonly Dictionary<(FontRole Role, int Dpi), Font> _fonts = [];
    private readonly Dictionary<(IconName Icon, float Scale), (GraphicsPath Stroke, GraphicsPath? Fill)> _icons = [];
    private readonly Dictionary<(float W, float H, float R), GraphicsPath> _rounded = [];
    private readonly Dictionary<(int W, int H, int R, int Dpi, int Argb), Bitmap> _shadows = [];
    private readonly Dictionary<Font, (int Baseline, float Central)> _metrics = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    public ThemeResources(ThemeTokens tokens) => Tokens = tokens;

    public ThemeTokens Tokens { get; }

    public SolidBrush Brush(Color color)
    {
        int key = color.ToArgb();
        lock (_gate)
        {
            if (!_brushes.TryGetValue(key, out SolidBrush? brush))
            {
                brush = new SolidBrush(color);
                _brushes[key] = brush;
            }

            return brush;
        }
    }

    /// <summary>A pen of <paramref name="width"/> device pixels; round caps and joins when <paramref name="round"/>.</summary>
    public Pen Pen(Color color, float width, bool round = false)
    {
        (int, float, bool) key = (color.ToArgb(), width, round);
        lock (_gate)
        {
            if (!_pens.TryGetValue(key, out Pen? pen))
            {
                pen = new Pen(color, width);
                if (round)
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                }

                _pens[key] = pen;
            }

            return pen;
        }
    }

    /// <summary>A font of a type token, created in pixels for the DPI. Weight 600 uses Segoe UI Semibold.</summary>
    public Font Font(FontRole role, int dpi)
    {
        lock (_gate)
        {
            if (!_fonts.TryGetValue((role, dpi), out Font? font))
            {
                (string family, float pixels, _) = ThemeService.FontSpec(role);
                font = new Font(family, pixels * dpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
                _fonts[(role, dpi)] = font;
            }

            return font;
        }
    }

    /// <summary>
    /// Where a font's text sits, for positioning by vertical centre as the design files do: the baseline of
    /// a line whose centre is y lies at y + <c>Central</c> (rounded half up), and GDI draws that baseline
    /// <c>Baseline</c> pixels below the top of its text rectangle (measured once per font).
    /// </summary>
    public (int Baseline, float Central) TextMetrics(Font font)
    {
        lock (_gate)
        {
            if (_metrics.TryGetValue(font, out (int, float) metrics))
            {
                return metrics;
            }
        }

        FontFamily family = font.FontFamily;
        float em = family.GetEmHeight(font.Style);
        float size = font.Size;
        float ascent = MathF.Round(family.GetCellAscent(font.Style) * size / em, MidpointRounding.AwayFromZero);
        float descent = MathF.Round(family.GetCellDescent(font.Style) * size / em, MidpointRounding.AwayFromZero);
        int baseline = MeasureBaseline(font);
        (int, float) result = (baseline, (ascent - descent) / 2f);
        lock (_gate)
        {
            _metrics[font] = result;
        }

        return result;
    }

    private static int MeasureBaseline(Font font)
    {
        int height = font.Height * 2 + 8;
        using Bitmap probe = new(Math.Max(16, font.Height * 2), height, PixelFormat.Format32bppRgb);
        using (Graphics g = Graphics.FromImage(probe))
        {
            g.Clear(Color.Black);
            TextRenderer.DrawText(g, "H", font, new Rectangle(2, 0, probe.Width - 2, height), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < probe.Width; x++)
            {
                if (probe.GetPixel(x, y).R > 128)
                {
                    return y + 1;
                }
            }
        }

        return (int)Math.Round(font.Size);
    }

    /// <summary>The paths of an icon at a scale (device pixels per grid unit), at the origin.</summary>
    public (GraphicsPath Stroke, GraphicsPath? Fill) Icon(IconName icon, float scale)
    {
        lock (_gate)
        {
            if (!_icons.TryGetValue((icon, scale), out (GraphicsPath, GraphicsPath?) paths))
            {
                paths = IconGeometry.Build(icon, scale);
                _icons[(icon, scale)] = paths;
            }

            return paths;
        }
    }

    /// <summary>A rounded rectangle at the origin.</summary>
    public GraphicsPath Rounded(float width, float height, float radius)
    {
        lock (_gate)
        {
            if (!_rounded.TryGetValue((width, height, radius), out GraphicsPath? path))
            {
                path = new GraphicsPath();
                IconGeometry.AddRoundedRect(path, 0, 0, width, height, radius);
                _rounded[(width, height, radius)] = path;
            }

            return path;
        }
    }

    /// <summary>
    /// The shadow of a rounded rectangle of the given device size: a Gaussian blur (standard deviation
    /// 10 px) of the shape in the Shadow colour, clipped to the SVG filter region (−20 % to +120 % across,
    /// −20 % to +130 % down). The bitmap's origin is at (−margin, −margin) relative to the shape.
    /// </summary>
    public (Bitmap Bitmap, int Margin) Shadow(int width, int height, int radius, int dpi)
    {
        Color color = Tokens.Shadow;
        int margin = (int)Math.Ceiling(ThemeService.Sf(dpi, 30));
        lock (_gate)
        {
            if (!_shadows.TryGetValue((width, height, radius, dpi, color.ToArgb()), out Bitmap? bitmap))
            {
                bitmap = ShadowRenderer.Render(width, height, radius, ThemeService.Sf(dpi, 10), margin, color);
                _shadows[(width, height, radius, dpi, color.ToArgb())] = bitmap;
            }

            return (bitmap, margin);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (SolidBrush brush in _brushes.Values)
            {
                brush.Dispose();
            }

            foreach (Pen pen in _pens.Values)
            {
                pen.Dispose();
            }

            foreach (Font font in _fonts.Values)
            {
                font.Dispose();
            }

            foreach ((GraphicsPath stroke, GraphicsPath? fill) in _icons.Values)
            {
                stroke.Dispose();
                fill?.Dispose();
            }

            foreach (GraphicsPath path in _rounded.Values)
            {
                path.Dispose();
            }

            foreach (Bitmap bitmap in _shadows.Values)
            {
                bitmap.Dispose();
            }

            _brushes.Clear();
            _pens.Clear();
            _fonts.Clear();
            _icons.Clear();
            _rounded.Clear();
            _shadows.Clear();
        }
    }
}

/// <summary>Renders the blurred shadow bitmaps once per size.</summary>
internal static class ShadowRenderer
{
    public static Bitmap Render(int width, int height, int radius, float sigma, int margin, Color color)
    {
        int w = width + 2 * margin;
        int h = height + 2 * margin;
        float[] alpha = new float[w * h];

        // Coverage of the rounded rectangle, offset 6 px down (scaled with sigma's DPI factor).
        float offset = sigma * 0.6f;
        using (Bitmap mask = new(w, h, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(mask))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using GraphicsPath shape = new();
                IconGeometry.AddRoundedRect(shape, margin, margin + offset, width, height, radius);
                g.FillPath(Brushes.White, shape);
            }

            BitmapData data = mask.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = (byte*)data.Scan0 + y * data.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            alpha[y * w + x] = row[x * 4 + 3] / 255f;
                        }
                    }
                }
            }
            finally
            {
                mask.UnlockBits(data);
            }
        }

        float[] blurred = GaussianBlur(alpha, w, h, sigma);

        // The SVG filter region: x from -20% to +120% of the width, y from -20% to +130% of the height,
        // measured from the offset shape.
        float left = margin - 0.2f * width;
        float right = margin + 1.2f * width;
        float top = margin + offset - 0.2f * height;
        float bottom = margin + offset + 1.3f * height;
        Bitmap result = new(w, h, PixelFormat.Format32bppPArgb);
        BitmapData output = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = (byte*)output.Scan0 + y * output.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        bool inside = x + 0.5f >= left && x + 0.5f <= right && y + 0.5f >= top && y + 0.5f <= bottom;
                        float a = inside ? blurred[y * w + x] * color.A / 255f : 0;
                        byte alphaByte = (byte)Math.Clamp((int)Math.Round(a * 255), 0, 255);
                        row[x * 4] = (byte)(color.B * alphaByte / 255);
                        row[x * 4 + 1] = (byte)(color.G * alphaByte / 255);
                        row[x * 4 + 2] = (byte)(color.R * alphaByte / 255);
                        row[x * 4 + 3] = alphaByte;
                    }
                }
            }
        }
        finally
        {
            result.UnlockBits(output);
        }

        return result;
    }

    private static float[] GaussianBlur(float[] source, int w, int h, float sigma)
    {
        int radius = (int)Math.Ceiling(sigma * 3);
        float[] kernel = new float[2 * radius + 1];
        float sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            kernel[i + radius] = MathF.Exp(-(i * i) / (2 * sigma * sigma));
            sum += kernel[i + radius];
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        float[] temp = new float[w * h];
        float[] result = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                float value = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = x + k;
                    if (sx >= 0 && sx < w)
                    {
                        value += source[rowStart + sx] * kernel[k + radius];
                    }
                }

                temp[rowStart + x] = value;
            }
        }

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float value = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sy = y + k;
                    if (sy >= 0 && sy < h)
                    {
                        value += temp[sy * w + x] * kernel[k + radius];
                    }
                }

                result[y * w + x] = value;
            }
        }

        return result;
    }
}
