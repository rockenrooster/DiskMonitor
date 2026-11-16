using System.Runtime.InteropServices;
using DiskMonitor.Diagnostics;
using Microsoft.Win32;

namespace DiskMonitor.UI.Theming;

/// <summary>The type tokens of subplan 02. No other sizes or weights exist.</summary>
internal enum FontRole
{
    Caption,
    CaptionStrong,
    Body,
    BodyStrong,
    Subtitle,
    Title,
    Metric,
    Mono
}

/// <summary>Implemented by controls that host a native child, so they can re-colour it on a theme change.</summary>
internal interface IThemeAware
{
    void ApplyTheme();
}

/// <summary>
/// Resolves System / Light / Dark (high contrast wins when Windows has it on), owns the cached GDI
/// resources, follows Windows when its setting changes, and scales logical pixels for a DPI.
/// Switching repaints; no window is rebuilt.
/// </summary>
internal static class ThemeService
{
    private const string SegoeUi = "Segoe UI";
    private const string SegoeUiSemibold = "Segoe UI Semibold";
    private const string Consolas = "Consolas";
    private const int DwmUseImmersiveDarkMode = 20;

    private static string _setting = "System";
    private static bool _systemHooked;

    public static ThemeTokens Tokens { get; private set; } = ThemeTokens.Dark;

    public static ThemeResources Resources { get; private set; } = new(ThemeTokens.Dark);

    /// <summary>Raised on the UI thread when the resolved token set changed.</summary>
    public static event Action? ThemeChanged;

    /// <summary>Forces a DPI for every control. The render harness only.</summary>
    public static int? DpiOverride { get; set; }

    /// <summary>The render harness turns animations off so drawers and toasts are captured in place.</summary>
    public static bool ForceNoAnimation { get; set; }

    /// <summary>False when Windows' "Animation effects" is off: the drawer and toasts then appear at once.</summary>
    public static bool AnimationsEnabled
    {
        get
        {
            if (ForceNoAnimation)
            {
                return false;
            }

            int enabled = 1;
            return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) || enabled != 0;
        }
    }

    /// <summary>Applies "System", "Light" or "Dark". High contrast wins when it is on.</summary>
    public static void Apply(string setting)
    {
        _setting = setting;
        if (!_systemHooked)
        {
            _systemHooked = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        Set(Resolve(setting));
    }

    /// <summary>Applies a token set directly. The render harness and tests only.</summary>
    public static void Set(ThemeTokens tokens)
    {
        bool changed = !ReferenceEquals(tokens, Tokens) && tokens != Tokens;
        Tokens = tokens;
        try
        {
            Application.SetColorMode(tokens.IsDark ? SystemColorMode.Dark : SystemColorMode.Classic);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            // The colour mode is fixed once a window exists on some Windows builds; native leftovers keep it.
        }

        if (changed)
        {
            ThemeResources old = Resources;
            Resources = new ThemeResources(tokens);
            old.Dispose();
            ThemeChanged?.Invoke();
        }
    }

    public static ThemeTokens Resolve(string setting)
    {
        if (SystemInformation.HighContrast)
        {
            return ThemeTokens.HighContrast();
        }

        return setting switch
        {
            "Light" => ThemeTokens.Light,
            "Dark" => ThemeTokens.Dark,
            _ => SystemPrefersDark() ? ThemeTokens.Dark : ThemeTokens.Light
        };
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Theme query", ex);
            return true;
        }
    }

    public static int Dpi(Control control) => DpiOverride ?? control.DeviceDpi;

    /// <summary>Scales a logical pixel measure to device pixels for the control's DPI.</summary>
    public static int S(Control control, int value) => S(Dpi(control), value);

    public static int S(int dpi, int value) => (int)Math.Round(value * dpi / 96.0, MidpointRounding.AwayFromZero);

    public static float Sf(int dpi, float value) => value * dpi / 96f;

    public static Font Font(Control control, FontRole role) => Resources.Font(role, Dpi(control));

    /// <summary>Font family, weight and pixel size of a type token.</summary>
    public static (string Family, float Pixels, int Weight) FontSpec(FontRole role) => role switch
    {
        FontRole.Caption => (SegoeUi, 11, 400),
        FontRole.CaptionStrong => (SegoeUiSemibold, 11, 600),
        FontRole.Body => (SegoeUi, 13, 400),
        FontRole.BodyStrong => (SegoeUiSemibold, 13, 600),
        FontRole.Subtitle => (SegoeUiSemibold, 16, 600),
        FontRole.Title => (SegoeUiSemibold, 20, 600),
        FontRole.Metric => (SegoeUiSemibold, 28, 600),
        _ => (Consolas, 12, 400)
    };

    /// <summary>DWM attribute 20: a dark or light caption to match the theme.</summary>
    public static void ApplyWindowChrome(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int dark = Tokens.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    /// <summary>Native leftovers (text-box internals, scroll bars) follow the theme.</summary>
    public static void ApplyNativeTheme(Control control)
    {
        if (control.IsHandleCreated)
        {
            _ = SetWindowTheme(control.Handle, Tokens.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            ThemeTokens next = Resolve(_setting);
            if (next != Tokens)
            {
                Set(next);
            }
        }
    }

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", EntryPoint = "SetWindowTheme", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
}
