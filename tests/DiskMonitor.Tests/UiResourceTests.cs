using System.Runtime.InteropServices;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Tests;

/// <summary>The virtual list scrolls without allocating (UI-07); theme switches leak no GDI or USER objects (UI-01).</summary>
public sealed class UiResourceTests
{
    private sealed class Row(int index) : ListRow
    {
        public override object Key { get; } = index;

        public override int LogicalHeight => index % 25 == 0 ? 32 : 44;

        public override bool Selectable => index % 25 != 0;

        public override string AccessibleName => "row";
    }

    [Fact]
    public void FiftyThousandRowsScrollWithoutAllocating()
    {
        RunSta(() =>
        {
            using VirtualList list = new() { Size = new Size(800, 600) };
            Row[] rows = Enumerable.Range(0, 50_000).Select(i => new Row(i)).ToArray();
            list.SetRows(rows);
            int sink = 0;

            // Warm up (JIT tiering may allocate once), then measure 1,000 scroll steps, each with the row
            // lookups painting would do.
            int Steps(int count)
            {
                int total = 0;
                for (int i = 0; i < count; i++)
                {
                    list.ScrollBy(i % 2 == 0 ? 137 : -61);
                    int first = list.IndexAt(list.ScrollOffset);
                    for (int r = Math.Max(0, first); r < Math.Min(rows.Length, first + 20); r++)
                    {
                        total += list.RowBounds(r).Height;
                    }
                }

                return total;
            }

            for (int round = 0; round < 5; round++)
            {
                sink += Steps(1_000);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            sink += Steps(1_000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(sink != 0);
            Assert.True(allocated == 0, $"{allocated} bytes allocated over 1,000 scroll steps");
        });
    }

    [Fact]
    public void SetRowsKeepsTheSelectionByIdentity()
    {
        RunSta(() =>
        {
            using VirtualList list = new() { Size = new Size(800, 600) };
            list.SetRows(Enumerable.Range(0, 100).Select(i => new Row(i)).ToArray());
            list.SelectOnly(10);
            list.SetRows(Enumerable.Range(0, 120).Select(i => new Row(i)).ToArray());
            Assert.Single(list.SelectedKeys);
            Assert.Equal(10, list.SelectedKeys.First());
            Assert.Equal(10, list.FocusIndex);
        });
    }

    [Fact]
    public void TwoHundredThemeSwitchesLeakNothing()
    {
        RunSta(() =>
        {
            ThemeTokens original = ThemeService.Tokens;
            using Form form = new()
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000),
                ShowInTaskbar = false,
                ClientSize = new Size(1280, 800)
            };
            SheetLike sheet = new() { Dock = DockStyle.Fill };
            form.Controls.Add(sheet);
            form.Show();
            Application.DoEvents();
            for (int i = 0; i < 10; i++)
            {
                Switch(i);
            }

            IntPtr process = GetCurrentProcess();
            int gdiBefore = GetGuiResources(process, 0);
            int userBefore = GetGuiResources(process, 1);
            for (int i = 0; i < 200; i++)
            {
                Switch(i);
            }

            int gdiAfter = GetGuiResources(process, 0);
            int userAfter = GetGuiResources(process, 1);
            ThemeService.Set(original);
            Assert.True(gdiAfter <= gdiBefore * 1.05 + 2, $"GDI objects {gdiBefore} -> {gdiAfter}");
            Assert.True(userAfter <= userBefore * 1.05 + 2, $"USER objects {userBefore} -> {userAfter}");

            void Switch(int i)
            {
                ThemeService.Set(i % 2 == 0 ? ThemeTokens.Dark : ThemeTokens.Light);
                sheet.Refresh();
                Application.DoEvents();
            }
        });
    }

    /// <summary>A canvas with one of every kit control and painter, so every cached resource is exercised.</summary>
    private sealed class SheetLike : KitControl
    {
        public SheetLike()
        {
            Controls.Add(new KitButton { Text = "Scan", Icon = IconName.Play, Variant = ButtonVariant.Primary, Bounds = new Rectangle(20, 20, 120, 32) });
            Controls.Add(new Chip { Text = "High", Count = "3", Severity = Level.High, Bounds = new Rectangle(160, 20, 100, 28) });
            Controls.Add(new KitSwitch { Checked = true, Bounds = new Rectangle(280, 26, 40, 20) });
            Controls.Add(new KitCheckBox { Checked = true, Text = "Check", Bounds = new Rectangle(340, 26, 100, 20) });
            DropDown dropDown = new() { Bounds = new Rectangle(460, 20, 200, 32) };
            dropDown.SetItems(["One", "Two"]);
            Controls.Add(dropDown);
            SegmentedControl segmented = new() { Bounds = new Rectangle(680, 20, 240, 32) };
            segmented.SetItems(["Tier", "Drive", "None"]);
            Controls.Add(segmented);
        }

        protected override void PaintContent(Graphics g)
        {
            Surfaces.Card(g, new RectangleF(20, 80, 400, 200), "CARD", Dpi);
            Surfaces.InfoBar(g, new RectangleF(20, 300, 600, 40), Level.High, "Info", "Action", Dpi);
            Badges.Severity(g, 20, 360, Level.Critical, "Critical", Dpi);
            Draw.Shadow(g, new Rectangle(500, 100, 300, 120), 8, Dpi);
            DataGraphics.Donut(g, 900, 200, 40, 10, 0.7, T.OkFill);
            foreach (IconName icon in Enum.GetValues<IconName>())
            {
                Draw.Icon(g, icon, 20 + (int)icon * 20, 420, 16, Dpi, T.Text);
            }
        }
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

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
