using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DiskMonitor.UI.Theming;

/// <summary>The tray badge: the state the tray icon shows (design/tokens.md, Tray badge).</summary>
internal enum TrayBadge
{
    Ok,
    Low,
    Medium,
    High,
    Critical,
    Snoozed
}

/// <summary>
/// Rasterises Disk Monitor's app icon from the geometry in design/icons.md: the standard version at 32 px
/// and up, the small version at 24 px and below. The app icon is the one fixed-colour drawing.
/// </summary>
internal static class IconArt
{
    private static readonly Color GradientTop = Color.FromArgb(0x6E, 0xD3, 0xFF);
    private static readonly Color GradientBottom = Color.FromArgb(0x1F, 0x6F, 0xE0);
    private static readonly Color SmallBody = Color.FromArgb(0x2B, 0x8C, 0xEB);

    public static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>Renders the icon at <paramref name="size"/> pixels on a transparent background.</summary>
    public static Bitmap Render(int size)
    {
        Bitmap bitmap = new(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        Paint(g, 0, 0, size);
        return bitmap;
    }

    /// <summary>Paints the icon into a square of <paramref name="size"/> pixels at (x, y).</summary>
    public static void Paint(Graphics g, float x, float y, float size)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        float k = size / 256f;
        bool small = size <= 24;
        PointF P(float px, float py) => new(x + px * k, y + py * k);

        using (GraphicsPath body = new())
        {
            IconGeometry.AddRoundedRect(body, x + 24 * k, y + 24 * k, 208 * k, 208 * k, 40 * k);
            if (small)
            {
                using SolidBrush flat = new(SmallBody);
                g.FillPath(flat, body);
            }
            else
            {
                using LinearGradientBrush gradient = new(new PointF(0, y + 24 * k), new PointF(0, y + 232 * k), GradientTop, GradientBottom);
                g.FillPath(gradient, body);
            }
        }

        float stroke = (small ? 22 : 14) * k;
        using Pen pen = new(Color.White, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using (GraphicsPath drive = new())
        {
            if (small)
            {
                IconGeometry.AddRoundedRect(drive, x + 50 * k, y + 66 * k, 156 * k, 124 * k, 20 * k);
            }
            else
            {
                IconGeometry.AddRoundedRect(drive, x + 48 * k, y + 68 * k, 160 * k, 120 * k, 20 * k);
            }

            g.DrawPath(pen, drive);
        }

        if (small)
        {
            g.DrawLine(pen, P(50, 150), P(206, 150));
            g.FillEllipse(Brushes.White, x + (174 - 12) * k, y + (170 - 12) * k, 24 * k, 24 * k);
        }
        else
        {
            g.DrawLine(pen, P(48, 148), P(208, 148));
            g.FillEllipse(Brushes.White, x + (176 - 9) * k, y + (168 - 9) * k, 18 * k, 18 * k);
            g.DrawLines(pen, [P(68, 108), P(98, 108), P(114, 82), P(136, 130), P(152, 108), P(188, 108)]);
        }
    }

    /// <summary>
    /// The tray icon: the small version with the status badge in the bottom-right corner (diameter 9 at
    /// 16 px), in the dark theme's colours with a 1.5 px outline in the dark Bg. The caller owns the icon.
    /// </summary>
    public static Icon CreateTrayIcon(int size, TrayBadge badge)
    {
        using Bitmap bitmap = Render(size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            PaintBadge(g, size, badge);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = System.Drawing.Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    public static void PaintBadge(Graphics g, int size, TrayBadge badge)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        ThemeTokens dark = ThemeTokens.Dark;
        float diameter = 9f * size / 16f;
        float x = size - diameter;
        float y = size - diameter;
        float scale = diameter / 16f;
        float outline = 1.5f * size / 16f;
        Color fill = badge switch
        {
            TrayBadge.Critical => dark.CritFill,
            TrayBadge.High => dark.HighFill,
            TrayBadge.Medium => dark.MedFill,
            TrayBadge.Low => dark.LowFill,
            TrayBadge.Snoozed => dark.TextDisabled,
            _ => dark.OkFill
        };

        using GraphicsPath shape = new();
        PointF P(float px, float py) => new(x + px * scale, y + py * scale);
        switch (badge)
        {
            case TrayBadge.Critical:
                shape.AddPolygon([P(5, 1.5f), P(11, 1.5f), P(14.5f, 5), P(14.5f, 11), P(11, 14.5f), P(5, 14.5f), P(1.5f, 11), P(1.5f, 5)]);
                break;
            case TrayBadge.High:
                shape.AddPolygon([P(8, 1.5f), P(15, 14.5f), P(1, 14.5f)]);
                break;
            case TrayBadge.Medium:
                shape.AddPolygon([P(8, 1), P(15, 8), P(8, 15), P(1, 8)]);
                break;
            default:
                shape.AddEllipse(x + 1 * scale, y + 1 * scale, 14 * scale, 14 * scale);
                break;
        }

        using Pen ring = new(dark.Bg, outline) { LineJoin = LineJoin.Round };
        g.DrawPath(ring, shape);
        using SolidBrush brush = new(fill);
        g.FillPath(brush, shape);
        if (badge == TrayBadge.Snoozed)
        {
            using Pen bar = new(dark.Bg, Math.Max(1, 1.5f * size / 16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(bar, P(4.5f, 8), P(11.5f, 8));
        }
    }

    /// <summary>Writes the toast icon (a PNG).</summary>
    public static void SavePng(string path, int size)
    {
        using Bitmap bitmap = Render(size);
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>
    /// Writes a standard multi-image icon: ICONDIR, one ICONDIRENTRY per image, then the images. The 256 px
    /// image is a PNG; smaller ones are 32-bit bitmaps with an AND mask.
    /// </summary>
    public static void SaveIco(string path)
    {
        List<byte[]> images = [];
        foreach (int size in IcoSizes)
        {
            using Bitmap bitmap = Render(size);
            images.Add(size >= 256 ? Png(bitmap) : Dib(bitmap));
        }

        using FileStream file = new(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)IcoSizes.Length);
        int offset = 6 + 16 * IcoSizes.Length;
        for (int i = 0; i < IcoSizes.Length; i++)
        {
            int size = IcoSizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (byte[] image in images)
        {
            writer.Write(image);
        }
    }

    private static byte[] Png(Bitmap bitmap)
    {
        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] Dib(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int maskStride = (size + 31) / 32 * 4;
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(size * size * 4 + maskStride * size);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        byte[] mask = new byte[maskStride * size];
        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                writer.Write(c.B);
                writer.Write(c.G);
                writer.Write(c.R);
                writer.Write(c.A);
                if (c.A == 0)
                {
                    int row = size - 1 - y;
                    mask[row * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        writer.Write(mask);
        return stream.ToArray();
    }

    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    private static extern bool DestroyIcon(IntPtr icon);
}
