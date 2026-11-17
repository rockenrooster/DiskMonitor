using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Pages;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Tests;

/// <summary>
/// Chips, drop-downs and buttons size to their content. When a count, a label or the on state changes, the
/// page asks the shell to arrange it again; before, the control kept its old width until the page was shown
/// again, and its text overflowed or was cut off. Layouts that arranging causes are not passed on.
/// </summary>
[Collection("UI")]
public sealed class PageLayoutTests
{
    [Fact]
    public void ContentChangesAskTheShellToArrangeThePage()
    {
        RunSta(() =>
        {
            ThemeService.Set(ThemeTokens.Light);
            using ProbePage page = new();
            Chip chip = new() { Text = "Critical" };
            DropDown drop = new();
            page.Controls.Add(chip);
            page.Controls.Add(drop);
            int requests = 0;
            page.LayoutRequested += (_, _) => requests++;

            chip.Count = "1,234";
            Assert.Equal(1, requests);
            chip.Count = "1,234";
            Assert.Equal(1, requests);
            chip.On = true;
            Assert.Equal(2, requests);
            drop.SetItems(["All drives", "Sabrent Rocket 4.0 1TB"]);
            Assert.Equal(3, requests);
            drop.SelectedIndex = 1;
            Assert.Equal(4, requests);

            // What arranging does to the children must not ask again.
            chip.SetBounds(0, 0, chip.PreferredWidth, 28);
            drop.SetBounds(0, 40, drop.PreferredWidth(96), 28);
            chip.Visible = false;
            page.SetBounds(0, 0, 800, 600);
            Assert.Equal(4, requests);
        });
    }

    private sealed class ProbePage() : PageBase(null!, null!)
    {
        public override string Key => "Probe";

        public override string Title => "Probe";

        public override int Arrange(int width, int viewportHeight) => viewportHeight;
    }

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
