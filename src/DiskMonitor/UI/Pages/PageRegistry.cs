using DiskMonitor.App;

namespace DiskMonitor.UI.Pages;

/// <summary>Creates the real pages for the shell (a page not built yet falls back to a placeholder).</summary>
internal static class PageRegistry
{
    public static void Register() => MainForm.PageFactory = Create;

    public static PageBase Create(AppServices services, INavigator navigator, string key) => key switch
    {
        "Overview" => new OverviewPage(services, navigator),
        "Events" => new EventsPage(services, navigator),
        "Drives" => new DrivesPage(services, navigator),
        "CheckDisk" => new CheckDiskPage(services, navigator),
        _ => new PlaceholderPage(services, navigator, key, MainForm.PageTitle(key))
    };
}
