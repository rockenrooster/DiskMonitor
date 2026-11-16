using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;

namespace DiskMonitor.UI.Theming;

/// <summary>Colour tokens of one theme, exactly as <c>docs/plans/design/tokens.md</c> lists them.</summary>
internal sealed record ThemeTokens
{
    public required string Name { get; init; }
    public bool IsDark { get; init; }
    public bool IsHighContrast { get; init; }

    public required Color Bg { get; init; }
    public required Color Surface { get; init; }
    public required Color Raised { get; init; }
    public required Color Hover { get; init; }
    public required Color Pressed { get; init; }
    public required Color Selected { get; init; }
    public required Color Stroke { get; init; }
    public required Color StrokeStrong { get; init; }
    public required Color Text { get; init; }
    public required Color Text2 { get; init; }
    public required Color Text3 { get; init; }
    public required Color TextDisabled { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    public required Color OnAccent { get; init; }
    public required Color DangerFill { get; init; }
    public required Color DangerHover { get; init; }
    public required Color DangerPressed { get; init; }
    public required Color OnDanger { get; init; }
    public required Color CritText { get; init; }
    public required Color CritFill { get; init; }
    public required Color CritPill { get; init; }
    public required Color HighText { get; init; }
    public required Color HighFill { get; init; }
    public required Color HighPill { get; init; }
    public required Color MedText { get; init; }
    public required Color MedFill { get; init; }
    public required Color MedPill { get; init; }
    public required Color LowText { get; init; }
    public required Color LowFill { get; init; }
    public required Color LowPill { get; init; }
    public required Color OkText { get; init; }
    public required Color OkFill { get; init; }
    public required Color OkPill { get; init; }
    public required Color NeutralPill { get; init; }
    public required Color Scrim { get; init; }
    public required Color Shadow { get; init; }

    /// <summary>Text on a selected item; differs from <see cref="Text"/> only in high contrast.</summary>
    public Color OnSelected { get; init; }

    public Color TierText(Tier tier) => tier switch
    {
        Tier.Critical => CritText,
        Tier.High => HighText,
        Tier.Medium => MedText,
        _ => LowText
    };

    public Color TierFill(Tier tier) => tier switch
    {
        Tier.Critical => CritFill,
        Tier.High => HighFill,
        Tier.Medium => MedFill,
        _ => LowFill
    };

    public Color TierPill(Tier tier) => tier switch
    {
        Tier.Critical => CritPill,
        Tier.High => HighPill,
        Tier.Medium => MedPill,
        _ => LowPill
    };

    /// <summary>Drive health reuses the severity tokens (design/tokens.md, Drive health).</summary>
    public (Color Text, Color Fill, Color Pill) HealthColors(HealthStatus health) => health switch
    {
        HealthStatus.Good => (OkText, OkFill, OkPill),
        HealthStatus.Caution => (MedText, MedFill, MedPill),
        HealthStatus.Bad => (CritText, CritFill, CritPill),
        _ => (Text2, Text3, NeutralPill)
    };

    /// <summary>Severity level of an info bar, badge or toast: a tier, OK, or neutral.</summary>
    public (Color Text, Color Fill, Color Pill) LevelColors(Level level) => level switch
    {
        Level.Critical => (CritText, CritFill, CritPill),
        Level.High => (HighText, HighFill, HighPill),
        Level.Medium => (MedText, MedFill, MedPill),
        Level.Low => (LowText, LowFill, LowPill),
        Level.Ok => (OkText, OkFill, OkPill),
        _ => (Text2, Text3, NeutralPill)
    };

    public static readonly ThemeTokens Dark = new()
    {
        Name = "Dark",
        IsDark = true,
        Bg = Hex(0x1F1F1F),
        Surface = Hex(0x2A2A2A),
        Raised = Hex(0x333333),
        Hover = Hex(0x3A3A3A),
        Pressed = Hex(0x474747),
        Selected = Hex(0x243C4D),
        Stroke = Hex(0x454545),
        StrokeStrong = Hex(0x8F8F8F),
        Text = Hex(0xF5F5F5),
        Text2 = Hex(0xCECECE),
        Text3 = Hex(0xB0B0B0),
        TextDisabled = Hex(0x7A7A7A),
        Accent = Hex(0x60CDFF),
        AccentHover = Hex(0x8AD9FF),
        AccentPressed = Hex(0x3FB4E8),
        OnAccent = Hex(0x00131F),
        DangerFill = Hex(0xC93237),
        DangerHover = Hex(0xD13C41),
        DangerPressed = Hex(0xA8262B),
        OnDanger = Hex(0xFFFFFF),
        CritText = Hex(0xFF8585),
        CritFill = Hex(0xE5484D),
        CritPill = Hex(0x4C2F30),
        HighText = Hex(0xFFA657),
        HighFill = Hex(0xF76B15),
        HighPill = Hex(0x4F3626),
        MedText = Hex(0xFFD23F),
        MedFill = Hex(0xFFB224),
        MedPill = Hex(0x504229),
        LowText = Hex(0x85BDFF),
        LowFill = Hex(0x3E8BFF),
        LowPill = Hex(0x2E3B50),
        OkText = Hex(0x5AD694),
        OkFill = Hex(0x30A46C),
        OkPill = Hex(0x2B4036),
        NeutralPill = Hex(0x3A3A3A),
        Scrim = Color.FromArgb(128, 0, 0, 0),
        Shadow = Color.FromArgb(128, 0, 0, 0),
        OnSelected = Hex(0xF5F5F5)
    };

    public static readonly ThemeTokens Light = new()
    {
        Name = "Light",
        IsDark = false,
        Bg = Hex(0xF6F6F6),
        Surface = Hex(0xFFFFFF),
        Raised = Hex(0xFFFFFF),
        Hover = Hex(0xEBEBEB),
        Pressed = Hex(0xDCDCDC),
        Selected = Hex(0xD6E8F8),
        Stroke = Hex(0xD0D0D0),
        StrokeStrong = Hex(0x6E6E6E),
        Text = Hex(0x1A1A1A),
        Text2 = Hex(0x404040),
        Text3 = Hex(0x595959),
        TextDisabled = Hex(0x9A9A9A),
        Accent = Hex(0x005FB8),
        AccentHover = Hex(0x0A70CF),
        AccentPressed = Hex(0x004A91),
        OnAccent = Hex(0xFFFFFF),
        DangerFill = Hex(0xC4252B),
        DangerHover = Hex(0xD13438),
        DangerPressed = Hex(0xA61D22),
        OnDanger = Hex(0xFFFFFF),
        CritText = Hex(0xB91C22),
        CritFill = Hex(0xD13438),
        CritPill = Hex(0xF9E7E7),
        HighText = Hex(0xA54300),
        HighFill = Hex(0xD45A00),
        HighPill = Hex(0xFAEBE0),
        MedText = Hex(0x735400),
        MedFill = Hex(0xA37A00),
        MedPill = Hex(0xF6F1E0),
        LowText = Hex(0x1A56B0),
        LowFill = Hex(0x2B6FD6),
        LowPill = Hex(0xE6EEFA),
        OkText = Hex(0x146C3E),
        OkFill = Hex(0x1F8A52),
        OkPill = Hex(0xE4F1EA),
        NeutralPill = Hex(0xEBEBEB),
        Scrim = Color.FromArgb(77, 0, 0, 0),
        Shadow = Color.FromArgb(56, 0, 0, 0),
        OnSelected = Hex(0x1A1A1A)
    };

    /// <summary>
    /// High contrast follows the Windows contrast theme (design/tokens.md, High contrast): surfaces
    /// are Window, text and lines WindowText, accent and selection Highlight. Built when it is applied,
    /// because the user picks the colours.
    /// </summary>
    public static ThemeTokens HighContrast()
    {
        Color window = SystemColors.Window;
        Color text = SystemColors.WindowText;
        Color highlight = SystemColors.Highlight;
        Color onHighlight = SystemColors.HighlightText;
        Color gray = SystemColors.GrayText;
        return new ThemeTokens
        {
            Name = "HighContrast",
            IsDark = window.GetBrightness() < 0.5f,
            IsHighContrast = true,
            Bg = window,
            Surface = window,
            Raised = window,
            Hover = window,
            Pressed = window,
            Selected = highlight,
            Stroke = text,
            StrokeStrong = text,
            Text = text,
            Text2 = text,
            Text3 = text,
            TextDisabled = gray,
            Accent = highlight,
            AccentHover = highlight,
            AccentPressed = highlight,
            OnAccent = onHighlight,
            DangerFill = highlight,
            DangerHover = highlight,
            DangerPressed = highlight,
            OnDanger = onHighlight,
            CritText = text,
            CritFill = text,
            CritPill = window,
            HighText = text,
            HighFill = text,
            HighPill = window,
            MedText = text,
            MedFill = text,
            MedPill = window,
            LowText = text,
            LowFill = text,
            LowPill = window,
            OkText = text,
            OkFill = text,
            OkPill = window,
            NeutralPill = window,
            Scrim = Color.FromArgb(128, 0, 0, 0),
            Shadow = Color.Transparent,
            OnSelected = onHighlight
        };
    }

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}

/// <summary>Severity of a non-event element (info bar, badge, toast): a tier, OK, or neutral.</summary>
internal enum Level
{
    Neutral,
    Low,
    Medium,
    High,
    Critical,
    Ok
}

internal static class LevelExtensions
{
    public static Level ToLevel(this Tier tier) => tier switch
    {
        Tier.Critical => Level.Critical,
        Tier.High => Level.High,
        Tier.Medium => Level.Medium,
        _ => Level.Low
    };

    public static Level ToLevel(this HealthStatus health) => health switch
    {
        HealthStatus.Good => Level.Ok,
        HealthStatus.Caution => Level.Medium,
        HealthStatus.Bad => Level.Critical,
        _ => Level.Neutral
    };
}
