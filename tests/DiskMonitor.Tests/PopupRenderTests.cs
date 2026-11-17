using System.Drawing;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Tests;

/// <summary>
/// Floating surfaces (menus, drop-down lists, tooltips, toasts) are composed over their shadow without
/// resampling: every pixel inside the rounded outline is the opaque surface's pixel, so ClearType text
/// stays as crisp as it is in the window.
/// </summary>
[Collection("UI")]
public sealed class PopupRenderTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void MenuSurfaceIsCopiedPixelForPixel(int dpi)
    {
        RunSta(() =>
        {
            ThemeService.Set(ThemeTokens.Dark);
            using KitMenu menu = new([new MenuEntry("Copy event"), new MenuEntry("Open in Event Viewer"), MenuEntry.Separator, new MenuEntry("Mark as read")], dpi);
            using Bitmap surface = menu.RenderSurface()!;
            using Bitmap composed = menu.Compose()!;
            int margin = (composed.Width - surface.Width) / 2;
            int inset = ThemeService.S(dpi, 10);
            int mismatches = 0;
            for (int y = inset; y < surface.Height - inset; y++)
            {
                for (int x = inset; x < surface.Width - inset; x++)
                {
                    Color expected = surface.GetPixel(x, y);
                    Color actual = composed.GetPixel(x + margin, y + margin);
                    if (actual.A != 255 || actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
                    {
                        mismatches++;
                    }
                }
            }

            Assert.Equal(0, mismatches);
        });
    }

    /// <summary>
    /// Pop-up text is drawn through a real GDI surface: on a GDI+ bitmap's Graphics, ClearType edges were
    /// blended against an empty scratch copy and grey text (the shortcut, Text3) came out near-black.
    /// </summary>
    [Fact]
    public void MenuShortcutKeepsItsColour()
    {
        RunSta(() =>
        {
            ThemeService.Set(ThemeTokens.Light);
            using KitMenu menu = new([new MenuEntry("Details", null, "Ctrl+Enter")], 96);
            using Bitmap surface = menu.RenderSurface()!;
            double floor = Luma(ThemeTokens.Light.Text3) - 40;
            double darkest = 255;
            for (int y = 4; y < 36; y++)
            {
                for (int x = surface.Width - 110; x < surface.Width - 8; x++)
                {
                    darkest = Math.Min(darkest, Luma(surface.GetPixel(x, y)));
                }
            }

            Assert.True(darkest < 200, "The shortcut was not drawn.");
            Assert.True(darkest >= floor, $"The shortcut came out darker ({darkest:0}) than its colour allows ({floor:0}).");
        });
    }

    private static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
