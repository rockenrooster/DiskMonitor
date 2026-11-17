using System.Drawing;
using DiskMonitor.App;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Diagnostics;
using DiskMonitor.UI.Pages;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Tests;

/// <summary>
/// The scanning row's progress ring in the volumes table turns while a run is going: the page repaints the
/// ring on the shared animation clock, not only when progress arrives. Before, the ring stood still
/// between updates while the one in the navigation rail turned.
/// </summary>
[Collection("UI")]
public sealed class CheckDiskPageTests
{
    [Fact]
    public void ScanningRowRingAnimatesOnlyWhileARunIsGoing()
    {
        RunSta(() =>
        {
            ThemeService.Set(ThemeTokens.Light);
            using AppServices services = new(action => action(), demoMode: true);
            DemoData.Populate(services);
            using CheckDiskPage page = new(services, null!);

            page.RefreshData(RefreshReason.All);
            Assert.False(page.RingAnimating);
            Assert.Null(page.RunningRowRing());

            DemoData.ScanRunning(services);
            page.RefreshData(RefreshReason.Chkdsk);
            Assert.True(page.RingAnimating);
            Rectangle ring = Assert.IsType<Rectangle>(page.RunningRowRing());
            Assert.True(ring.Width > 0 && ring.Height > 0);

            DemoData.ScanFinished(services, ChkdskOutcome.Clean);
            page.RefreshData(RefreshReason.Chkdsk);
            Assert.False(page.RingAnimating);
        });
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
