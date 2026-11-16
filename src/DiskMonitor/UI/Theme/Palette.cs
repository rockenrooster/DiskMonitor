using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;

namespace DiskMonitor.UI.Theming;

/// <summary>Every color the UI uses, for one appearance. Controls never hard-code a color.</summary>
internal sealed record Palette
{
    public required bool IsDark { get; init; }

    /// <summary>The window behind everything.</summary>
    public required Color Window { get; init; }
    public required Color Sidebar { get; init; }

    /// <summary>Cards and panels that sit on the window.</summary>
    public required Color Surface { get; init; }

    /// <summary>Inputs, secondary buttons, and insets that sit on a surface.</summary>
    public required Color SurfaceAlt { get; init; }
    public required Color SurfaceHover { get; init; }
    public required Color SurfacePressed { get; init; }
    public required Color Border { get; init; }
    public required Color BorderStrong { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color TextFaint { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    public required Color OnAccent { get; init; }
    public required Color Critical { get; init; }
    public required Color High { get; init; }
    public required Color Medium { get; init; }
    public required Color Low { get; init; }
    public required Color Good { get; init; }

    public static Palette Dark { get; } = new()
    {
        IsDark = true,
        Window = Hex(0x101216),
        Sidebar = Hex(0x0B0D10),
        Surface = Hex(0x181B21),
        SurfaceAlt = Hex(0x20242C),
        SurfaceHover = Hex(0x2B313D),
        SurfacePressed = Hex(0x13161B),
        Border = Hex(0x2A2F3A),
        BorderStrong = Hex(0x4A5265),
        Text = Hex(0xECEFF4),
        TextMuted = Hex(0xA3ACBB),
        TextFaint = Hex(0x717A8A),
        Accent = Hex(0x4C8DFF),
        AccentHover = Hex(0x7AABFF),
        AccentPressed = Hex(0x2F6BD6),
        OnAccent = Hex(0xFFFFFF),
        Critical = Hex(0xF2495C),
        High = Hex(0xFF8B3E),
        Medium = Hex(0xF2C744),
        Low = Hex(0x56B6F7),
        Good = Hex(0x36D399)
    };

    public static Palette Light { get; } = new()
    {
        IsDark = false,
        Window = Hex(0xF3F5F9),
        Sidebar = Hex(0xE8ECF3),
        Surface = Hex(0xFFFFFF),
        SurfaceAlt = Hex(0xF1F4F8),
        SurfaceHover = Hex(0xDFE5EE),
        SurfacePressed = Hex(0xCBD3E0),
        Border = Hex(0xD6DCE6),
        BorderStrong = Hex(0x9AA5B8),
        Text = Hex(0x161A22),
        TextMuted = Hex(0x535E70),
        TextFaint = Hex(0x808A9B),
        Accent = Hex(0x2563EB),
        AccentHover = Hex(0x1949B8),
        AccentPressed = Hex(0x12378C),
        OnAccent = Hex(0xFFFFFF),
        Critical = Hex(0xD92D3F),
        High = Hex(0xD9621A),
        Medium = Hex(0xA87900),
        Low = Hex(0x1877C9),
        Good = Hex(0x0E8F62)
    };

    public Color ForTier(Tier tier) => tier switch
    {
        Tier.Critical => Critical,
        Tier.High => High,
        Tier.Medium => Medium,
        _ => Low
    };

    public Color ForHealth(HealthStatus health) => health switch
    {
        HealthStatus.Good => Good,
        HealthStatus.Caution => Medium,
        HealthStatus.Bad => Critical,
        _ => TextFaint
    };

    /// <summary>A translucent wash of <paramref name="color"/> over a surface, for badges and selected rows.</summary>
    public Color Tint(Color color, Color over, double amount = 0.16) => Draw.Blend(over, color, amount);

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
