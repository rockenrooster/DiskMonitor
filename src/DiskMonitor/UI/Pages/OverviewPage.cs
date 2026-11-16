using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>
/// Overview (subplan 04): "Are my drives OK, and what should I do next?" in one glance. Every element is
/// clickable and lands on a pre-filtered view. One view model is rebuilt per refresh; painting reads only it.
/// </summary>
internal sealed class OverviewPage : PageBase
{
    private readonly KitButton _refresh = new() { Icon = IconName.Refresh, Variant = ButtonVariant.Subtle, ToolTipText = Strings.OverviewRefreshTip, AccessibleName = Strings.OverviewRefreshTip };
    private readonly List<InfoBarControl> _infoBars = [];
    private readonly TargetCard _drivesCard = new() { Title = Strings.CardDrives };
    private readonly TargetCard _eventsCard = new() { Title = Strings.CardEvents };
    private readonly TargetCard _checkCard = new() { Title = Strings.CardCheckDisk };
    private readonly TargetCard[] _tiles = new TargetCard[4];
    private readonly TargetCard _byDrive = new() { Title = Strings.CardByDrive };
    private readonly TargetCard _steps = new() { Title = Strings.CardNextSteps };
    private readonly TargetCard _recent = new() { Title = Strings.CardRecentEvents };
    private readonly List<KitButton> _stepButtons = [];
    private ViewModel _model = ViewModel.Empty;
    private bool _historyLoaded;
    private bool _startupEnabled = true;
    private ToastSetting _toastSetting = ToastSetting.Enabled;

    public OverviewPage(AppServices services, INavigator navigator) : base(services, navigator)
    {
        _refresh.Click += (_, _) =>
        {
            _ = Services.Monitor.ReloadAsync();
            _ = Services.Drives.RefreshAsync();
        };

        _drivesCard.WholeCardAction = () => Navigator.ShowPage("Drives");
        _eventsCard.WholeCardAction = () => Navigator.ShowEvents(new EventFilter(UnreadOnly: true));
        _checkCard.WholeCardAction = () => Navigator.ShowPage("CheckDisk");
        _drivesCard.Painter = PaintDrivesCard;
        _eventsCard.Painter = PaintEventsCard;
        _checkCard.Painter = PaintCheckCard;
        Tier[] tiers = TierExtensions.Descending;
        for (int i = 0; i < 4; i++)
        {
            Tier tier = tiers[i];
            TargetCard tile = new() { IsTile = true, AccessibleName = Strings.TierName(tier) };
            tile.WholeCardAction = () => Navigator.ShowEvents(new EventFilter(Tiers: new HashSet<Tier> { tier }));
            tile.Painter = (g, card) => PaintTile(g, card, tier);
            _tiles[i] = tile;
        }

        _byDrive.Painter = PaintByDrive;
        _byDrive.SetKeyboardTargets(true);
        _steps.Painter = PaintSteps;
        _recent.Painter = PaintRecent;
        _recent.SetKeyboardTargets(true);
        foreach (Control control in new Control[] { _drivesCard, _eventsCard, _checkCard }.Concat(_tiles).Concat([_byDrive, _steps, _recent]))
        {
            Controls.Add(control);
        }
    }

    public override string Key => "Overview";

    public override string Title => Strings.PageOverview;

    public override IReadOnlyList<Control> Toolbar => [_refresh];

    public override void Activate()
    {
        // Process and registry queries run here, never in painting. Demo mode shows a machine with no extras.
        _startupEnabled = Services.DemoMode || StartupManager.IsEnabled();
        _toastSetting = Services.DemoMode ? ToastSetting.Enabled : Services.Notifier.QueryWindowsSetting();
    }

    public override void RefreshData(RefreshReason reasons)
    {
        if (!Services.Monitor.IsLoading)
        {
            _historyLoaded = true;
        }

        _model = ViewModel.Build(Services, _historyLoaded, _startupEnabled, _toastSetting);
        SyncInfoBars();
        SyncStepButtons();
        RequestLayout();
        Invalidate(true);
    }

    public override int Arrange(int width, int viewportHeight)
    {
        int y = 0;
        foreach (InfoBarControl bar in _infoBars)
        {
            bar.SetBounds(0, y, width, S(40));
            y += S(40) + S(12);
        }

        int gap = S(12);
        int cardWidth = (width - 2 * gap) / 3;
        _drivesCard.SetBounds(0, y, cardWidth, S(136));
        _eventsCard.SetBounds(cardWidth + gap, y, cardWidth, S(136));
        _checkCard.SetBounds(2 * (cardWidth + gap), y, width - 2 * (cardWidth + gap), S(136));
        y += S(136) + gap;

        int tileWidth = (width - 3 * gap) / 4;
        for (int i = 0; i < 4; i++)
        {
            int x = i * (tileWidth + gap);
            _tiles[i].SetBounds(x, y, i == 3 ? width - x : tileWidth, S(88));
        }

        y += S(88) + gap;
        int byDriveHeight = S(48) + S(36) * Math.Max(1, _model.Drives.Count);
        _byDrive.SetBounds(0, y, width, byDriveHeight);
        y += byDriveHeight + gap;

        int recentWidth = S(340);
        int bottomHeight = Math.Max(S(264), viewportHeight - y);
        _steps.SetBounds(0, y, width - recentWidth - gap, bottomHeight);
        _recent.SetBounds(width - recentWidth, y, recentWidth, bottomHeight);
        LayoutStepButtons();
        return y + bottomHeight;
    }

    private void SyncInfoBars()
    {
        List<(Level Level, string Text, string? Action)> wanted = [];
        if (_model.MonitorError is { } error)
        {
            wanted.Add((Level.High, Strings.InfoLimited(error), null));
        }

        if (_model.SnoozedUntil is { } until)
        {
            wanted.Add((Level.Low, Strings.InfoSnoozed(until), Strings.InfoResume));
        }

        if (_model.Loading)
        {
            wanted.Add((Level.Low, Strings.InfoReadingHistory(Services.Settings.HistoryDays), null));
        }

        while (_infoBars.Count > wanted.Count)
        {
            InfoBarControl last = _infoBars[^1];
            _infoBars.RemoveAt(_infoBars.Count - 1);
            Controls.Remove(last);
            last.Dispose();
        }

        while (_infoBars.Count < wanted.Count)
        {
            InfoBarControl bar = new() { Backdrop = static t => t.Bg };
            bar.ActionClicked += (_, _) => Services.Notifier.Snooze(null);
            _infoBars.Add(bar);
            Controls.Add(bar);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            _infoBars[i].Configure(wanted[i].Level, wanted[i].Text, wanted[i].Action);
        }
    }

    private void SyncStepButtons()
    {
        foreach (KitButton button in _stepButtons)
        {
            _steps.Controls.Remove(button);
            button.Dispose();
        }

        _stepButtons.Clear();
        for (int i = 0; i < _model.Steps.Count; i++)
        {
            Step step = _model.Steps[i];
            KitButton button = new()
            {
                Text = step.Button,
                Compact = true,
                Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Secondary,
                Backdrop = static t => t.Surface
            };
            button.Click += (_, _) => RunStep(step);
            _stepButtons.Add(button);
            _steps.Controls.Add(button);
        }
    }

    private void LayoutStepButtons()
    {
        for (int i = 0; i < _stepButtons.Count; i++)
        {
            KitButton button = _stepButtons[i];
            button.SizeToContent();
            int rowTop = S(38) + i * S(52);
            button.Location = new Point(_steps.Width - S(16) - button.Width, rowTop + (S(52) - button.Height) / 2);
        }
    }

    private void RunStep(Step step)
    {
        switch (step.Kind)
        {
            case StepKind.OpenDrive:
                Navigator.ShowDrive(step.Disk ?? 0);
                break;
            case StepKind.ReviewCritical:
                Navigator.ShowEvents(new EventFilter(new HashSet<Tier> { Tier.Critical }, UnreadOnly: true));
                break;
            case StepKind.ReviewHigh:
                Navigator.ShowEvents(new EventFilter(new HashSet<Tier> { Tier.High }, UnreadOnly: true));
                break;
            case StepKind.Scan:
                Navigator.ShowCheckDisk(step.Volume, new ScanOfferReason(step.ReportedUtc));
                break;
            case StepKind.NotificationSettings:
                Shell.Open("ms-settings:notifications");
                break;
            case StepKind.TurnOnStartup:
                if (StartupManager.SetEnabled(true, Services.Settings.StartMinimized))
                {
                    _startupEnabled = true;
                    Navigator.ShowToast(new InAppToast(Level.Ok, Strings.RecStartupToast, null, Strings.Undo, UndoStartup));
                    Navigator.SetUndo(UndoStartup);
                    RefreshData(RefreshReason.Settings);
                }

                break;
            case StepKind.CheckDisk:
                Navigator.ShowPage("CheckDisk");
                break;
        }

        void UndoStartup()
        {
            _ = StartupManager.SetEnabled(false, Services.Settings.StartMinimized);
            _startupEnabled = false;
            RefreshData(RefreshReason.Settings);
        }
    }

    // --- painting --------------------------------------------------------------------------------------

    private void PaintDrivesCard(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        Color fill = card.SurfaceColor;
        if (_model.Loading || _model.Reporting == 0)
        {
            DataGraphics.Donut(g, 64 * s, 80 * s, 40 * s, 10 * s, 0, t.OkFill);
            Draw.Text(g, Strings.Dash, F(FontRole.Metric), 124 * s, 62 * s, t.Text);
            if (!_model.Loading)
            {
                Draw.Text(g, Strings.DrivesNoneReport, F(FontRole.Caption), 124 * s, 92 * s, t.Text2, (int)(card.Width - 140 * s));
            }

            return;
        }

        DataGraphics.Donut(g, 64 * s, 80 * s, 40 * s, 10 * s, _model.Healthy / (double)_model.Reporting, t.OkFill);
        Draw.Severity(g, Level.Ok, 54 * s, 70 * s, 20, Dpi, t.OkText, fill);
        Font metric = F(FontRole.Metric);
        string number = Strings.DrivesOfHealthy(_model.Healthy, _model.Reporting);
        Draw.Text(g, number, metric, 124 * s, 62 * s, t.Text);
        Draw.Text(g, Strings.DrivesHealthySuffix, F(FontRole.Body), 124 * s + Draw.TextWidth(number, metric), 68 * s, t.Text2);
        List<string> captions = [];
        if (_model.Attention > 0)
        {
            captions.Add(Strings.DrivesNeedAttention(_model.Attention));
        }

        if (_model.NoData > 0)
        {
            captions.Add(Strings.DrivesNoHealthData(_model.NoData));
        }

        PaintCaptions(g, card, captions, 124 * s);
    }

    private void PaintEventsCard(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        if (_model.Loading)
        {
            Draw.Text(g, Strings.Dash, F(FontRole.Metric), 16 * s, 62 * s, t.Text);
            return;
        }

        Font metric = F(FontRole.Metric);
        string number = Format.Number(_model.UnreadAttention);
        Draw.Text(g, number, metric, 16 * s, 62 * s, t.Text);
        Draw.Text(g, Strings.EventsUnreadSuffix, F(FontRole.Body), 16 * s + Draw.TextWidth(number, metric), 68 * s, t.Text2);
        List<string> captions = [];
        if (_model.UnreadAttention == 0)
        {
            captions.Add(Strings.EventsNoNewProblems);
        }
        else if (_model.Latest is { } latest)
        {
            captions.Add(Strings.EventsLatest(latest.Title, latest.TimeUtc));
        }

        captions.Add(Strings.EventsInDays(_model.TotalEvents, Services.Settings.HistoryDays));
        PaintCaptions(g, card, captions, 16 * s);
    }

    // In a top card narrower than 320 px the first caption wraps to two lines and the second is hidden.
    private void PaintCaptions(Graphics g, TargetCard card, List<string> captions, float x)
    {
        float s = Sc;
        Font caption = F(FontRole.Caption);
        int width = (int)(card.Width - x - 16 * s);
        if (card.Width < 320 * s && captions.Count > 0)
        {
            List<string> lines = Draw.Wrap(captions[0], caption, width);
            for (int i = 0; i < Math.Min(2, lines.Count); i++)
            {
                Draw.Text(g, lines[i], caption, x, (92 + 18 * i) * s, T.Text2, width);
            }

            return;
        }

        for (int i = 0; i < Math.Min(2, captions.Count); i++)
        {
            Draw.Text(g, captions[i], caption, x, (92 + 18 * i) * s, T.Text2, width);
        }
    }

    private void PaintCheckCard(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        Font label = F(FontRole.Caption);
        Font body = F(FontRole.Body);
        int width = (int)(card.Width - 80 * s);
        Draw.Text(g, Strings.CheckDiskLast, label, 16 * s, 52 * s, t.Text3);
        Draw.Text(g, _model.LastScan ?? Strings.CheckDiskNeverScanned, body, 64 * s, 52 * s, t.Text, width);
        float line = (float)Math.Floor(86 * s) + 0.5f;
        Draw.Line(g, t.Stroke, s, 16 * s, line, card.Width - 16 * s, line);
        Draw.Text(g, Strings.CheckDiskNext, label, 16 * s, 112 * s, t.Text3);
        Draw.Text(g, _model.NextScan ?? Strings.CheckDiskNoRoutine, body, 64 * s, 112 * s, t.Text, width);
    }

    private void PaintTile(Graphics g, TargetCard card, Tier tier)
    {
        ThemeTokens t = T;
        float s = Sc;
        Color fill = card.SurfaceColor;
        Draw.Severity(g, tier, 16 * s, 16 * s, 16, Dpi, fill);
        Draw.Text(g, Strings.TierName(tier), F(FontRole.Body), 40 * s, 24 * s, t.Text2);
        string count = _model.Loading ? Strings.Dash : Format.Number(_model.UnreadByTier.GetValueOrDefault(tier));
        Draw.Text(g, count, F(FontRole.Metric), 16 * s, 60 * s, t.Text);
        if (!_model.Loading)
        {
            Draw.TextRight(g, Strings.TilePlus24(_model.Last24ByTier.GetValueOrDefault(tier)), F(FontRole.Caption), card.Width - 16 * s, 66 * s, t.Text2);
        }
    }

    private void PaintByDrive(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        float barLeft = Math.Max(card.Width * 0.35f, 300 * s);
        float barRight = card.Width - 152 * s;
        for (int i = 0; i < _model.Drives.Count; i++)
        {
            DriveRow row = _model.Drives[i];
            RectangleF bounds = new(s, (36 + 36 * i) * s, card.Width - 2 * s, 36 * s);
            bool hover = card.PointerLocation is { } pointer && bounds.Contains(pointer);
            if (hover)
            {
                RowPaint.Background(g, bounds, true, false, 24 * s, Dpi);
            }

            float cy = bounds.Y + 18 * s;
            Color behind = hover ? t.Hover : t.Surface;
            Draw.Icon(g, IconName.Drive, 16 * s, cy - 8 * s, 16, Dpi, t.Text2);
            int badgeWidth = Badges.HealthWidth(row.Health, Dpi);
            float badgeX = barLeft - 12 * s - badgeWidth;
            Draw.Text(g, row.Name, F(FontRole.BodyStrong), 44 * s, cy, t.Text, (int)Math.Max(1, badgeX - 52 * s));
            Badges.Health(g, badgeX, cy - 10 * s, row.Health, Dpi);
            RectangleF bar = new(barLeft, cy - 4 * s, Math.Max(0, barRight - barLeft), 8 * s);
            List<(Tier Tier, RectangleF Bounds)> segments = DataGraphics.SeverityBar(g, bar, row.Counts, Dpi);
            if (segments.Count == 0)
            {
                Draw.FillRounded(g, bar, 2 * s, t.Stroke);
            }

            Draw.TextRight(g, Strings.DriveRowCount(row.Total), F(FontRole.Body), card.Width - 16 * s, cy, t.Text2);
            int disk = row.Number;
            card.AddTarget(new CardTarget(bounds, () => Navigator.ShowDrive(disk), row.Name));
            foreach ((Tier tier, RectangleF segment) in segments)
            {
                int count = row.Counts[tier];
                RectangleF hit = RectangleF.Inflate(segment, 0, 6 * s);
                card.AddTarget(new CardTarget(hit, () => Navigator.ShowEvents(new EventFilter(new HashSet<Tier> { tier }, disk)), Strings.SegmentTip(tier, count), Strings.SegmentTip(tier, count), Row: false));
            }

            if (i < _model.Drives.Count - 1)
            {
                RowPaint.Divider(g, 44 * s, card.Width - s, bounds.Bottom, Dpi);
            }
        }
    }

    private void PaintSteps(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        if (_model.Steps.Count == 0)
        {
            if (!_model.Loading)
            {
                Draw.TextCentered(g, Strings.NothingToDo, F(FontRole.Body), card.Width / 2f, card.Height / 2f, t.Text2);
            }

            return;
        }

        for (int i = 0; i < _model.Steps.Count; i++)
        {
            Step step = _model.Steps[i];
            float top = (38 + 52 * i) * s;
            Draw.Severity(g, step.Tier, 16 * s, top + 18 * s, 16, Dpi, t.Surface);
            float right = card.Width - 16 * s - (i < _stepButtons.Count ? _stepButtons[i].Width : 0) - 16 * s;
            Draw.Text(g, step.Title, F(FontRole.BodyStrong), 44 * s, top + 18 * s, t.Text, (int)(right - 44 * s));
            Draw.Text(g, step.Detail, F(FontRole.Caption), 44 * s, top + 36 * s, t.Text2, (int)(right - 44 * s));
            if (i < _model.Steps.Count - 1)
            {
                float line = (float)Math.Floor(top + 52 * s) + 0.5f;
                Draw.Line(g, t.Stroke, s, 16 * s, line, card.Width - 16 * s, line);
            }
        }
    }

    private void PaintRecent(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        int days = Services.Settings.HistoryDays;
        if (_model.Recent.Count == 0)
        {
            if (!_model.Loading)
            {
                Draw.TextCentered(g, Strings.RecentNone(days), F(FontRole.Body), card.Width / 2f, card.Height / 2f, t.Text2);
            }

            return;
        }

        float pitch = 30 * s;
        for (int i = 0; i < _model.Recent.Count; i++)
        {
            DiskEvent item = _model.Recent[i];
            float cy = 48 * s + i * pitch;
            bool hover = card.PointerLocation is { } pointer && Math.Abs(pointer.Y - cy) < pitch / 2f;
            DataGraphics.TimelineEntry(g, 23.5f * s, cy, 40 * s, card.Width - 16 * s, item.Title, Format.RecentDate(item.TimeUtc, inSentence: false), i == 0, i == _model.Recent.Count - 1, pitch, Dpi, hover ? t.Accent : null);
            string key = item.Key;
            card.AddTarget(new CardTarget(new RectangleF(8 * s, cy - pitch / 2f, card.Width - 16 * s, pitch), () => Navigator.ShowEvent(key), item.Title));
        }

        float captionY = 48 * s + _model.Recent.Count * pitch + 8 * s;
        Draw.Text(g, Strings.RecentPerDay(days), F(FontRole.Caption), 16 * s, captionY, t.Text3);
        RectangleF spark = new(16 * s, captionY + 14 * s, card.Width - 32 * s, 56 * s);
        if (spark.Bottom <= card.Height - 8 * s)
        {
            DataGraphics.Sparkline(g, spark, _model.PerDay, Dpi);
        }
    }

    // --- view model ---------------------------------------------------------------------------------------

    private enum StepKind
    {
        OpenDrive,
        ReviewCritical,
        Scan,
        ReviewHigh,
        NotificationSettings,
        TurnOnStartup,
        CheckDisk
    }

    private sealed record Step(Tier Tier, int Count, int Order, StepKind Kind, string Title, string Detail, string Button, int? Disk = null, string? Volume = null, DateTime? ReportedUtc = null);

    private sealed record DriveRow(int Number, string Name, HealthStatus Health, Dictionary<Tier, int> Counts, int Total);

    private sealed record ViewModel(
        bool Loading,
        string? MonitorError,
        DateTime? SnoozedUntil,
        int Healthy,
        int Reporting,
        int Attention,
        int NoData,
        int UnreadAttention,
        DiskEvent? Latest,
        int TotalEvents,
        string? LastScan,
        string? NextScan,
        Dictionary<Tier, int> UnreadByTier,
        Dictionary<Tier, int> Last24ByTier,
        List<DriveRow> Drives,
        List<Step> Steps,
        List<DiskEvent> Recent,
        List<double> PerDay)
    {
        public static readonly ViewModel Empty = new(true, null, null, 0, 0, 0, 0, 0, null, 0, null, null, [], [], [], [], [], []);

        public static ViewModel Build(AppServices services, bool historyLoaded, bool startupEnabled, ToastSetting toastSetting)
        {
            DateTime now = Clock.UtcNow;
            IReadOnlyList<DiskEvent> events = services.Events.Snapshot();
            UnreadSummary unread = services.Events.GetUnread();
            DriveSnapshot drives = services.Drives.Current;
            bool loading = !historyLoaded && services.Monitor.IsLoading;
            int days = services.Settings.HistoryDays;

            int healthy = drives.Disks.Count(d => d.Health == HealthStatus.Good);
            int reporting = drives.Disks.Count(d => d.Health != HealthStatus.Unknown);
            int attention = drives.Disks.Count(d => d.Health is HealthStatus.Caution or HealthStatus.Bad);
            int noData = drives.Disks.Count(d => d.Health == HealthStatus.Unknown);

            Dictionary<Tier, int> unreadByTier = TierExtensions.Descending.ToDictionary(tier => tier, unread.For);
            Dictionary<Tier, int> last24 = TierExtensions.Descending.ToDictionary(tier => tier, _ => 0);
            Dictionary<int, Dictionary<Tier, int>> perDisk = [];
            double[] perDay = new double[Math.Min(days, 90)];
            foreach (DiskEvent item in events)
            {
                if (now - item.TimeUtc <= TimeSpan.FromHours(24))
                {
                    last24[item.Tier]++;
                }

                if (item.DiskNumber is { } disk)
                {
                    if (!perDisk.TryGetValue(disk, out Dictionary<Tier, int>? counts))
                    {
                        counts = [];
                        perDisk[disk] = counts;
                    }

                    counts[item.Tier] = counts.GetValueOrDefault(item.Tier) + 1;
                }

                int daysAgo = (int)(now.ToLocalTime().Date - item.TimeLocal.Date).TotalDays;
                if (daysAgo >= 0 && daysAgo < perDay.Length)
                {
                    perDay[perDay.Length - 1 - daysAgo]++;
                }
            }

            List<DriveRow> driveRows = drives.Disks.OrderBy(d => d.Number).Select(d =>
            {
                Dictionary<Tier, int> counts = perDisk.GetValueOrDefault(d.Number) ?? [];
                return new DriveRow(d.Number, Strings.DriveRowName(d.DisplayName, d.VolumeLetters), d.Health, counts, counts.Values.Sum());
            }).ToList();

            Dictionary<string, ScanRecord> scans = services.State.Read(s => new Dictionary<string, ScanRecord>(s.Scans, StringComparer.OrdinalIgnoreCase));
            KeyValuePair<string, ScanRecord>? lastScan = scans.Count == 0 ? null : scans.MaxBy(p => p.Value.TimeUtc);
            string? lastText = lastScan is { } last ? Strings.CheckDiskLastValue(last.Key, last.Value.Clean, last.Value.TimeUtc) : null;
            string? nextText = services.Proactive.NextRoutineUtc is { } next ? Strings.CheckDiskNextValue(next) : null;

            List<DiskEvent> recent = [];
            HashSet<string> groups = [];
            foreach (DiskEvent item in events)
            {
                if (groups.Add(item.GroupKey))
                {
                    recent.Add(item);
                    if (recent.Count == 4)
                    {
                        break;
                    }
                }
            }

            List<Step> steps = loading ? [] : BuildSteps(services, events, drives, scans, unread, startupEnabled, toastSetting, now);
            return new ViewModel(
                loading,
                services.Monitor.LastError,
                services.Notifier.IsSnoozed ? services.Notifier.SnoozeUntilUtc : null,
                healthy,
                reporting,
                attention,
                noData,
                unread.Critical + unread.High + unread.Medium,
                events.Count > 0 ? events[0] : null,
                events.Count,
                lastText,
                nextText,
                unreadByTier,
                last24,
                driveRows,
                steps,
                recent,
                [.. perDay]);
        }

        private static List<Step> BuildSteps(AppServices services, IReadOnlyList<DiskEvent> events, DriveSnapshot drives, Dictionary<string, ScanRecord> scans, UnreadSummary unread, bool startupEnabled, ToastSetting toastSetting, DateTime now)
        {
            List<Step> steps = [];
            int order = 0;
            foreach (PhysicalDisk disk in drives.Disks.Where(d => d.Health == HealthStatus.Bad))
            {
                steps.Add(new Step(Tier.Critical, 1, order++, StepKind.OpenDrive, Strings.RecFailingTitle(disk.DisplayName), Strings.RecFailingDetail, Strings.RecOpenDrive, disk.Number));
            }

            DiskEvent? FirstUnread(Tier tier) => events.FirstOrDefault(e => e.Tier == tier && services.Events.IsUnread(e));
            if (unread.Critical > 0 && FirstUnread(Tier.Critical) is { } critical)
            {
                steps.Add(new Step(Tier.Critical, unread.Critical, order++, StepKind.ReviewCritical, Strings.RecCriticalTitle(unread.Critical), Strings.RecLatest(critical.Title, critical.DeviceText), Strings.RecReview));
            }

            HashSet<string> offered = new(StringComparer.OrdinalIgnoreCase);
            foreach (DiskEvent item in events)
            {
                if (item.SuggestScan && item.Volume is { } volume && offered.Add(volume)
                    && (!scans.TryGetValue(volume, out ScanRecord? record) || record.TimeUtc < item.TimeUtc)
                    && drives.FindVolume(volume) is { SupportsChkdsk: true })
                {
                    int count = events.Count(e => e.SuggestScan && string.Equals(e.Volume, volume, StringComparison.OrdinalIgnoreCase));
                    steps.Add(new Step(Tier.High, count, order++, StepKind.Scan, Strings.RecDamageTitle(volume), Strings.RecDamageDetail, Strings.RecScanButton(volume), Volume: volume, ReportedUtc: item.TimeUtc));
                }
            }

            if (unread.High > 0 && FirstUnread(Tier.High) is { } high)
            {
                steps.Add(new Step(Tier.High, unread.High, order++, StepKind.ReviewHigh, Strings.RecHighTitle(unread.High), Strings.RecLatest(high.Title, high.DeviceText), Strings.RecReview));
            }

            foreach (PhysicalDisk disk in drives.Disks.Where(d => d.Health == HealthStatus.Caution))
            {
                string note = disk.HealthNotes.Count > 0 ? disk.HealthNotes[0] : Strings.HealthName(disk.Health);
                steps.Add(new Step(Tier.Medium, 1, order++, StepKind.OpenDrive, Strings.RecCautionTitle(disk.DisplayName, note), Strings.RecCautionDetail, Strings.RecOpenDrive, disk.Number));
            }

            if (toastSetting is ToastSetting.DisabledForApplication or ToastSetting.DisabledForUser or ToastSetting.DisabledByGroupPolicy)
            {
                steps.Add(new Step(Tier.Medium, 1, order++, StepKind.NotificationSettings, Strings.RecBlockedTitle, Strings.RecBlockedDetail, Strings.RecOpenSettings));
            }

            if (!startupEnabled)
            {
                steps.Add(new Step(Tier.Low, 1, order++, StepKind.TurnOnStartup, Strings.RecStartupTitle, Strings.RecStartupDetail, Strings.RecTurnOn));
            }

            bool recentScan = scans.Values.Any(r => now - r.TimeUtc <= TimeSpan.FromDays(30));
            if (!recentScan && services.Settings.RoutineScan == "Off")
            {
                steps.Add(new Step(Tier.Low, 1, order++, StepKind.CheckDisk, Strings.RecNoScanTitle, Strings.RecNoScanDetail, Strings.RecCheckDisk));
            }

            // By tier, then by how many events or drives a row covers, then by table order; at most four.
            return steps.OrderByDescending(step => step.Tier).ThenByDescending(step => step.Count).ThenBy(step => step.Order).Take(4).ToList();
        }
    }
}

/// <summary>Opens links and folders through the shell.</summary>
internal static class Shell
{
    public static void Open(string target)
    {
        try
        {
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Diagnostics.ErrorLog.Write("Open", ex);
        }
    }
}
