using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskMonitor.UI.Theming;

internal enum FontRole
{
    Body,
    BodyBold,
    Small,
    SmallBold,
    Caption,
    Heading,
    Title,
    Metric,
    Mono,
    Icon,
    IconSmall,
    IconLarge,
    IconHuge
}

/// <summary>The active palette, DPI-scaled fonts and metrics, and native window theming.</summary>
internal static class Theme
{
    private static readonly Dictionary<(FontRole, int), Font> Fonts = [];
    private static readonly Lazy<string> IconFamily = new(() => FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets"));
    private static readonly Lazy<string> MonoFamily = new(() => FirstInstalled("Cascadia Mono", "Consolas"));
    private const string TextFamily = "Segoe UI";
    private const string StrongFamily = "Segoe UI Semibold";

    public static Palette Colors { get; private set; } = Palette.Dark;

    public static bool IsDark => Colors.IsDark;

    /// <summary>
    /// Selects the palette for a theme setting ("System", "Light", "Dark") and tells WinForms, so
    /// native parts (text boxes, menus, scroll bars) match. Call before creating windows.
    /// </summary>
    public static void Apply(string setting)
    {
        bool dark = setting.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            || (!setting.Equals("Light", StringComparison.OrdinalIgnoreCase) && SystemPrefersDark());
        Colors = dark ? Palette.Dark : Palette.Light;
        try
        {
            Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Older Windows builds lack native dark mode; the custom-drawn surfaces still follow the palette.
        }
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Scales a 96-DPI pixel measure to the monitor the control is on.</summary>
    public static int S(Control control, int value) => S(control.DeviceDpi, value);

    public static int S(int dpi, int value) => (int)Math.Round(value * dpi / 96.0);

    public static Font Font(Control control, FontRole role) => Font(control.DeviceDpi, role);

    /// <summary>
    /// Fonts are created in pixels for a specific DPI. Point-sized fonts would follow the primary
    /// monitor's DPI and come out the wrong size on a second monitor with different scaling.
    /// </summary>
    public static Font Font(int dpi, FontRole role)
    {
        lock (Fonts)
        {
            if (Fonts.TryGetValue((role, dpi), out Font? cached))
            {
                return cached;
            }

            (string family, float points) = role switch
            {
                FontRole.Body => (TextFamily, 9.75f),
                FontRole.BodyBold => (StrongFamily, 9.75f),
                FontRole.Small => (TextFamily, 8.75f),
                FontRole.SmallBold => (StrongFamily, 8.75f),
                FontRole.Caption => (StrongFamily, 8f),
                FontRole.Heading => (StrongFamily, 11.5f),
                FontRole.Title => (StrongFamily, 17f),
                FontRole.Metric => (StrongFamily, 21f),
                FontRole.Mono => (MonoFamily.Value, 9.25f),
                FontRole.IconSmall => (IconFamily.Value, 8.5f),
                FontRole.Icon => (IconFamily.Value, 11.5f),
                FontRole.IconLarge => (IconFamily.Value, 15f),
                _ => (IconFamily.Value, 24f)
            };

            Font font = new(family, points * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
            Fonts[(role, dpi)] = font;
            return font;
        }
    }

    /// <summary>Colors the title bar and frame to match the palette (Windows 10 20H1 and later).</summary>
    public static void ApplyWindowChrome(IntPtr handle)
    {
        const int useImmersiveDarkMode = 20;
        const int borderColor = 34;
        const int captionColor = 35;
        const int textColor = 36;
        int dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, useImmersiveDarkMode, ref dark, sizeof(int));
        int caption = ToColorRef(Colors.Sidebar);
        _ = DwmSetWindowAttribute(handle, captionColor, ref caption, sizeof(int));
        int text = ToColorRef(Colors.Text);
        _ = DwmSetWindowAttribute(handle, textColor, ref text, sizeof(int));
        int border = ToColorRef(Colors.Border);
        _ = DwmSetWindowAttribute(handle, borderColor, ref border, sizeof(int));
    }

    /// <summary>Gives a native control's scroll bars and edit area the dark Explorer look.</summary>
    public static void ApplyNativeTheme(Control control)
    {
        if (control.IsHandleCreated)
        {
            _ = SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static string FirstInstalled(params string[] families)
    {
        foreach (string family in families)
        {
            try
            {
                using FontFamily probe = new(family);
                return family;
            }
            catch (ArgumentException)
            {
                // Not installed; try the next one.
            }
        }

        return families[^1];
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
}
