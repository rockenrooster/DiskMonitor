using System.Diagnostics;
using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Events;
using DiskMonitor.Core.Notifications;
using DiskMonitor.Core.Updates;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>
/// Settings (subplan 08): every option on one page, each explained in a line and applied the moment it
/// changes. There is no Save button; changes that are expensive or hard to undo confirm first.
/// </summary>
internal sealed class SettingsPage : PageBase
{
    internal static readonly string[] SectionKeys = ["general", "alerts", "notifications", "monitoring", "rules", "updates", "advanced", "about"];

    private readonly SectionList _list;
    private readonly Dictionary<string, SettingsSectionView> _views = [];
    private SettingsSectionView _view;

    public SettingsPage(AppServices services, INavigator navigator) : base(services, navigator)
    {
        _list = new SectionList(this);
        Controls.Add(_list);
        _view = ViewFor(SectionKeys[0]);
        _view.Visible = true;
    }

    public override string Key => "Settings";

    public override string Title => Strings.PageSettings;

    internal AppServices AppServices => Services;

    internal INavigator Nav => Navigator;

    internal string CurrentSection => _view.Key;

    /// <summary>True when an update can be downloaded (the dot after Updates).</summary>
    internal bool UpdateReady => Services.Updates.LastResult?.CanDownload == true;

    internal static string SectionTitle(string key) => key switch
    {
        "general" => Strings.SectionGeneral,
        "alerts" => Strings.SectionAlerts,
        "notifications" => Strings.SectionNotifications,
        "monitoring" => Strings.SectionMonitoring,
        "rules" => Strings.SectionRules,
        "updates" => Strings.SectionUpdates,
        "advanced" => Strings.SectionAdvanced,
        _ => Strings.SectionAbout
    };

    public override void Navigate(object argument)
    {
        if (argument is not SettingsSection target)
        {
            return;
        }

        if (target.Section is { } section)
        {
            ShowSection(section);
        }

        if (target.StartUpdateCheck && _view is UpdatesSection updates)
        {
            updates.StartCheck();
        }
    }

    public override void Activate() => _view.Activate();

    public override void RefreshData(RefreshReason reasons)
    {
        _list.Invalidate();
        _view.OnRefresh(reasons);
        RequestLayout();
    }

    public override bool FocusSearch() => _view is RulesSection rules && rules.FocusFilter();

    public override int Arrange(int width, int viewportHeight)
    {
        _list.SetBounds(0, 0, S(168), SectionKeys.Length * S(36));
        int x = S(192);
        int sectionWidth = Math.Max(S(240), width - x);
        int height = _view.Arrange(sectionWidth);
        _view.SetBounds(x, 0, sectionWidth, height);
        return Math.Max(height, _list.Height);
    }

    internal void Relayout() => RequestLayout();

    /// <summary>Harness hook: shows a section and, for Rules, the info bar under a rule.</summary>
    internal void PreviewSection(string key, string? ruleId = null, string? info = null, string? filter = null)
    {
        ShowSection(key);
        if (_view is RulesSection rules && ruleId is not null && info is not null)
        {
            rules.PreviewInfo(ruleId, info, filter);
        }
    }

    /// <summary>Shows a section (the list, <c>ShowSettings(section)</c>, or Up and Down in the list).</summary>
    internal void ShowSection(string key)
    {
        string wanted = key.ToLowerInvariant();
        if (!SectionKeys.Contains(wanted))
        {
            wanted = SectionKeys[0];
        }

        if (wanted == _view.Key)
        {
            return;
        }

        _view.Visible = false;
        _view = ViewFor(wanted);
        _view.Activate();
        _view.Visible = true;
        _list.Invalidate();
        RequestLayout();
        (Parent as PageViewport)?.EnsureVisible(new Rectangle(0, 0, 1, 1));
    }

    private SettingsSectionView ViewFor(string key)
    {
        if (!_views.TryGetValue(key, out SettingsSectionView? view))
        {
            view = key switch
            {
                "general" => new GeneralSection(this),
                "alerts" => new AlertsSection(this),
                "notifications" => new NotificationsSection(this),
                "monitoring" => new MonitoringSection(this),
                "rules" => new RulesSection(this),
                "updates" => new UpdatesSection(this),
                "advanced" => new AdvancedSection(this),
                _ => new AboutSection(this)
            };
            view.Visible = false;
            view.Reload();
            _views[key] = view;
            Controls.Add(view);
        }

        return view;
    }

    /// <summary>A section whose content is one card of rows.</summary>
    private abstract class RowsSection : SettingsSectionView
    {
        private RowsCard? _card;

        protected RowsSection(SettingsPage page, string key, string title, string? intro) : base(page, key, title, intro)
        {
        }

        protected void SetRows(IReadOnlyList<SettingRow> rows)
        {
            _card = new RowsCard(rows);
            Controls.Add(_card);
        }

        protected override int ArrangeContent(int width, int top)
        {
            if (_card is null)
            {
                return top;
            }

            int height = _card.Measure(width);
            _card.SetBounds(0, top, width, height);
            _card.PerformLayout();
            _card.Invalidate();
            return top + height;
        }

        protected void Save()
        {
            Services.SaveSettings();
            _card?.PerformLayout();
            _card?.Invalidate(true);
        }

        protected static RowItem Item(Control control) => new() { Control = control };

        /// <summary>Painted text before a control (the quiet-hours <c>From</c> and <c>To</c>).</summary>
        protected RowItem Caption(string text, Func<bool> enabled) => new()
        {
            Painter = (g, x, cy) =>
            {
                Font body = F(FontRole.Body);
                if (g is not null)
                {
                    Draw.Text(g, text, body, x, cy, enabled() ? T.Text2 : T.TextDisabled);
                }

                return Draw.TextWidth(text, body);
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // General
    // ---------------------------------------------------------------------------------------------

    private sealed class GeneralSection : RowsSection
    {
        private readonly SegmentedControl _theme = new() { AccessibleName = Strings.SettingTheme };
        private readonly KitSwitch _startup = new() { AccessibleName = Strings.SettingStartup };
        private readonly KitSwitch _tray = new() { AccessibleName = Strings.SettingStartTray };
        private readonly KitSwitch _close = new() { AccessibleName = Strings.SettingCloseToTray };

        public GeneralSection(SettingsPage page) : base(page, "general", Strings.SectionGeneral, Strings.IntroGeneral)
        {
            _theme.SetItems([Strings.ThemeSystem, Strings.ThemeLight, Strings.ThemeDark]);
            SetRows(
            [
                new SettingRow { Label = Strings.SettingTheme, Items = [Item(_theme)] },
                new SettingRow { Label = Strings.SettingStartup, Helper = static () => Strings.SettingStartupHelp, Items = [Item(_startup)] },
                new SettingRow { Label = Strings.SettingStartTray, Helper = static () => Strings.SettingStartTrayHelp, Items = [Item(_tray)] },
                new SettingRow { Label = Strings.SettingCloseToTray, Items = [Item(_close)] }
            ]);
            AppSettings settings = Services.Settings;
            _theme.SelectionChanged += (_, _) =>
            {
                if (Loading)
                {
                    return;
                }

                settings.Theme = AppSettings.ThemeChoices[Math.Clamp(_theme.SelectedIndex, 0, AppSettings.ThemeChoices.Length - 1)];
                Save();
                ThemeService.Apply(settings.Theme);
            };
            _startup.CheckedChanged += (_, _) => { if (!Loading) { SetStartup(_startup.Checked); } };
            _tray.CheckedChanged += (_, _) =>
            {
                if (Loading)
                {
                    return;
                }

                settings.StartMinimized = _tray.Checked;
                Save();
                if (!Services.DemoMode)
                {
                    StartupManager.Refresh(settings.StartMinimized);
                }
            };
            _close.CheckedChanged += (_, _) => { if (!Loading) { settings.CloseToTray = _close.Checked; Save(); } };
        }

        public override void Reload() => Load(() =>
        {
            AppSettings settings = Services.Settings;
            _theme.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.ThemeChoices, settings.Theme));
            _startup.Checked = Services.DemoMode || StartupManager.IsEnabled();
            _tray.Checked = settings.StartMinimized;
            _close.Checked = settings.CloseToTray;
        });

        /// <summary>Writes or removes the Run value; on failure the switch goes back and a Critical toast says why.</summary>
        private void SetStartup(bool on)
        {
            if (Services.DemoMode)
            {
                Reload();
                return;
            }

            if (!StartupManager.SetEnabled(on, Services.Settings.StartMinimized))
            {
                Load(() => _startup.Checked = !on);
                Page.Nav.ShowToast(new InAppToast(Level.Critical, Strings.SettingFailed, Strings.SettingFailedReason));
                return;
            }

            Save();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Alerts
    // ---------------------------------------------------------------------------------------------

    private sealed class AlertsSection : RowsSection
    {
        private static readonly Tier[] Tiers = [Tier.Critical, Tier.High, Tier.Medium, Tier.Low];
        private static readonly string[] Columns = [Strings.ColumnNotify, Strings.ColumnSound, Strings.ColumnSticky];
        private readonly Dictionary<Tier, KitSwitch[]> _switches = [];

        public AlertsSection(SettingsPage page) : base(page, "alerts", Strings.SectionAlerts, Strings.IntroAlerts)
        {
            List<SettingRow> rows = [new SettingRow { Label = "", MinHeight = 32, PaintRow = PaintCaptions }];
            foreach (Tier tier in Tiers)
            {
                KitSwitch[] switches =
                [
                    new() { AccessibleName = Strings.AlertSwitchName(tier, Strings.ColumnNotify) },
                    new() { AccessibleName = Strings.AlertSwitchName(tier, Strings.ColumnSound), DisabledReason = Strings.NotifyFirst },
                    new() { AccessibleName = Strings.AlertSwitchName(tier, Strings.ColumnSticky), DisabledReason = Strings.NotifyFirst }
                ];
                _switches[tier] = switches;
                KitButton test = new() { Text = Strings.SendTest, Variant = ButtonVariant.Subtle, Compact = true, AccessibleName = Strings.AlertSwitchName(tier, Strings.SendTest) };
                test.Click += (_, _) =>
                {
                    if (!Services.DemoMode)
                    {
                        Services.Notifier.SendTest(tier);
                    }
                };
                for (int column = 0; column < switches.Length; column++)
                {
                    int index = column;
                    switches[column].CheckedChanged += (_, _) => OnSwitch(tier, index);
                }

                rows.Add(new SettingRow
                {
                    Label = Strings.TierName(tier),
                    Helper = () => Strings.TierHelp(tier),
                    PaintLabel = (g, x, cy) => Badges.Tier(g, x, cy - 10 * Sc, tier, Dpi),
                    Items =
                    [
                        new RowItem { Control = switches[0], CenterX = width => ColumnCenter(width, 0) },
                        new RowItem { Control = switches[1], CenterX = width => ColumnCenter(width, 1) },
                        new RowItem { Control = switches[2], CenterX = width => ColumnCenter(width, 2) },
                        Item(test)
                    ]
                });
            }

            SetRows(rows);
        }

        /// <summary>Centre of a 96 px column; the three columns end 112 px from the card's right edge.</summary>
        private int ColumnCenter(int width, int column) => width - S(112) - S(96) * (Columns.Length - column) + S(48);

        private void PaintCaptions(Graphics g, RectangleF row)
        {
            Font font = F(FontRole.CaptionStrong);
            for (int i = 0; i < Columns.Length; i++)
            {
                Draw.TextCentered(g, Columns[i], font, ColumnCenter((int)row.Width, i), row.Y + row.Height / 2f, T.Text3);
            }
        }

        private void OnSwitch(Tier tier, int column)
        {
            if (Loading)
            {
                return;
            }

            TierOptions options = Services.Settings.OptionsFor(tier);
            KitSwitch[] switches = _switches[tier];
            switch (column)
            {
                case 0:
                    options.Toast = switches[0].Checked;
                    switches[1].Enabled = switches[2].Enabled = options.Toast;
                    break;
                case 1:
                    options.Sound = switches[1].Checked;
                    break;
                default:
                    options.Sticky = switches[2].Checked;
                    break;
            }

            Save();
        }

        public override void Reload() => Load(() =>
        {
            foreach (Tier tier in Tiers)
            {
                TierOptions options = Services.Settings.OptionsFor(tier);
                KitSwitch[] switches = _switches[tier];
                switches[0].Checked = options.Toast;
                switches[1].Checked = options.Sound;
                switches[2].Checked = options.Sticky;
                switches[1].Enabled = switches[2].Enabled = options.Toast;
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Notifications
    // ---------------------------------------------------------------------------------------------

    private sealed class NotificationsSection : RowsSection
    {
        private static readonly int[] HalfHours = Enumerable.Range(0, 48).Select(i => i * 30).ToArray();
        private readonly KitButton _openSettings = new() { Text = Strings.OpenWindowsSettings, Compact = true };
        private readonly DropDown _snooze = new();
        private readonly KitSwitch _quiet = new() { AccessibleName = Strings.SettingQuietHours };
        private readonly DropDown _from = new();
        private readonly DropDown _to = new();
        private readonly KitSwitch _critical = new() { AccessibleName = Strings.SettingQuietCritical };
        private readonly DropDown _combine = new();
        private ToastSetting _status = ToastSetting.Unknown;
        private int _snoozeChoice;

        public NotificationsSection(SettingsPage page) : base(page, "notifications", Strings.SectionNotifications, Strings.IntroNotifications)
        {
            _snooze.SetItems([Strings.NotSnoozed, Strings.SnoozeHour, Strings.SnoozeFourHours, Strings.SnoozeTomorrow]);
            _from.SetItems(HalfHours.Select(Format.ClockTime));
            _to.SetItems(HalfHours.Select(Format.ClockTime));
            _combine.SetItems(AppSettings.CoalesceMinuteChoices.Select(Strings.MinutesChoice));
            NotificationService notifier = Services.Notifier;
            SetRows(
            [
                new SettingRow
                {
                    Label = Strings.SettingWindowsNotifications,
                    Helper = () => notifier.LastError is { } error ? Strings.LastNotificationFailed(error) : null,
                    HelperWarns = () => notifier.LastError is not null,
                    Items = [new RowItem { Painter = PaintStatus }, new RowItem { Control = _openSettings, Shown = () => Blocked }]
                },
                new SettingRow
                {
                    Label = Strings.SettingSnooze,
                    Helper = () => notifier.IsSnoozed ? Strings.SnoozedUntil(notifier.SnoozeUntilUtc) : Strings.SettingSnoozeHelp,
                    Items = [Item(_snooze)]
                },
                new SettingRow
                {
                    Label = Strings.SettingQuietHours,
                    Helper = static () => Strings.SettingQuietHoursHelp,
                    Items = [Caption(Strings.QuietFrom, () => _quiet.Checked), Item(_from), Caption(Strings.QuietTo, () => _quiet.Checked), Item(_to), Item(_quiet)]
                },
                new SettingRow { Label = Strings.SettingQuietCritical, Helper = static () => Strings.SettingQuietCriticalHelp, Items = [Item(_critical)] },
                new SettingRow { Label = Strings.SettingCombine, Helper = static () => Strings.SettingCombineHelp, Items = [Item(_combine)] }
            ]);

            // Reading order: the switch comes before the times it enables.
            _quiet.TabIndex = 0;
            _from.TabIndex = 1;
            _to.TabIndex = 2;
            _from.AccessibleDescription = Strings.QuietFrom;
            _to.AccessibleDescription = Strings.QuietTo;

            AppSettings settings = Services.Settings;
            _openSettings.Click += (_, _) => OpenWithShell("ms-settings:notifications");
            _snooze.SelectionChanged += (_, _) => OnSnooze();
            _quiet.CheckedChanged += (_, _) =>
            {
                if (!Loading)
                {
                    settings.QuietHoursEnabled = _quiet.Checked;
                    SetQuietEnabled();
                    Save();
                }
            };
            _from.SelectionChanged += (_, _) => { if (!Loading) { settings.QuietHoursStart = HalfHours[_from.SelectedIndex]; Save(); } };
            _to.SelectionChanged += (_, _) => { if (!Loading) { settings.QuietHoursEnd = HalfHours[_to.SelectedIndex]; Save(); } };
            _critical.CheckedChanged += (_, _) => { if (!Loading) { settings.QuietHoursAllowCritical = _critical.Checked; Save(); } };
            _combine.SelectionChanged += (_, _) =>
            {
                if (!Loading)
                {
                    settings.CoalesceMinutes = AppSettings.CoalesceMinuteChoices[Math.Clamp(_combine.SelectedIndex, 0, AppSettings.CoalesceMinuteChoices.Length - 1)];
                    Save();
                }
            };
        }

        private bool Blocked => _status is ToastSetting.DisabledForApplication or ToastSetting.DisabledForUser or ToastSetting.DisabledByGroupPolicy;

        public override void Activate()
        {
            // The Windows setting is read on the UI thread when the section is shown (subplan 08).
            _status = Services.DemoMode ? ToastSetting.Enabled : Services.Notifier.QueryWindowsSetting();
            base.Activate();
        }

        public override void OnRefresh(RefreshReason reasons)
        {
            if ((reasons & (RefreshReason.Settings | RefreshReason.Snooze)) != 0)
            {
                Reload();
            }
        }

        public override void Reload() => Load(() =>
        {
            AppSettings settings = Services.Settings;
            NotificationService notifier = Services.Notifier;
            if (!notifier.IsSnoozed)
            {
                _snoozeChoice = 0;
            }

            _snooze.SelectedIndex = SnoozeIndex();
            _quiet.Checked = settings.QuietHoursEnabled;
            _from.SelectedIndex = Math.Max(0, Array.IndexOf(HalfHours, settings.QuietHoursStart));
            _to.SelectedIndex = Math.Max(0, Array.IndexOf(HalfHours, settings.QuietHoursEnd));
            _critical.Checked = settings.QuietHoursAllowCritical;
            _combine.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.CoalesceMinuteChoices, settings.CoalesceMinutes));
            SetQuietEnabled();
        });

        private void SetQuietEnabled() => _from.Enabled = _to.Enabled = _critical.Enabled = _quiet.Checked;

        /// <summary>The choice that matches the current snooze: the user's own, or the nearest when it was set elsewhere.</summary>
        private int SnoozeIndex()
        {
            NotificationService notifier = Services.Notifier;
            if (!notifier.IsSnoozed)
            {
                return 0;
            }

            if (_snoozeChoice > 0)
            {
                return _snoozeChoice;
            }

            TimeSpan left = notifier.SnoozeUntilUtc - Clock.UtcNow;
            return left <= TimeSpan.FromHours(1) ? 1 : left <= TimeSpan.FromHours(4) ? 2 : 3;
        }

        private void OnSnooze()
        {
            if (Loading)
            {
                return;
            }

            _snoozeChoice = _snooze.SelectedIndex;
            NotificationService notifier = Services.Notifier;
            switch (_snoozeChoice)
            {
                case 1:
                    notifier.Snooze(TimeSpan.FromHours(1));
                    break;
                case 2:
                    notifier.Snooze(TimeSpan.FromHours(4));
                    break;
                case 3:
                    notifier.SnoozeUntil(Clock.UtcNow.ToLocalTime().Date.AddDays(1).AddHours(8).ToUniversalTime());
                    break;
                default:
                    notifier.Snooze(null);
                    break;
            }

            Save();
        }

        private float PaintStatus(Graphics? g, float x, float cy)
        {
            (string text, Level? level) = _status switch
            {
                ToastSetting.Enabled => (Strings.NotificationsAllowed, Level.Ok),
                ToastSetting.DisabledForApplication => (Strings.NotificationsOffForApp, Level.Medium),
                ToastSetting.DisabledForUser => (Strings.NotificationsOffForUser, Level.Medium),
                ToastSetting.DisabledByGroupPolicy => (Strings.NotificationsOffByPolicy, (Level?)Level.Medium),
                _ => (Strings.NotificationsUnknown, null)
            };
            Font body = F(FontRole.Body);
            float s = Sc;
            if (g is not null)
            {
                ThemeTokens t = T;
                if (level is { } shape)
                {
                    Draw.Severity(g, shape, x, cy - 8 * s, 16, Dpi, t.LevelColors(shape).Text, t.Surface);
                }
                else
                {
                    Draw.State(g, StateIcon.Skipped, x, cy - 8 * s, 16, Dpi, t.Text3);
                }

                Draw.Text(g, text, body, x + 24 * s, cy, t.Text2);
            }

            return 24 * s + Draw.TextWidth(text, body);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Monitoring
    // ---------------------------------------------------------------------------------------------

    private sealed class MonitoringSection : RowsSection
    {
        private readonly DropDown _history = new();
        private readonly KitSwitch _chkdsk = new() { AccessibleName = Strings.SettingIncludeChkdsk };
        private readonly KitSwitch _removable = new() { AccessibleName = Strings.SettingRemovable };
        private readonly DropDown _interval = new();
        private readonly DropDown _temperature = new();
        private readonly DropDown _lowSpace = new();

        public MonitoringSection(SettingsPage page) : base(page, "monitoring", Strings.SectionMonitoring, Strings.IntroMonitoring)
        {
            _history.SetItems(AppSettings.HistoryDayChoices.Select(Strings.DaysChoice));
            _interval.SetItems(AppSettings.HealthIntervalChoices.Select(Strings.MinutesChoice));
            _temperature.SetItems(AppSettings.TemperatureChoices.Select(c => Format.Temperature(c)));
            _lowSpace.SetItems(AppSettings.LowSpaceChoices.Select(Strings.PercentChoice));
            SetRows(
            [
                new SettingRow { Label = Strings.SettingHistory, Helper = static () => Strings.SettingHistoryHelp, Items = [Item(_history)] },
                new SettingRow { Label = Strings.SettingIncludeChkdsk, Helper = static () => Strings.SettingIncludeChkdskHelp, Items = [Item(_chkdsk)] },
                new SettingRow { Label = Strings.SettingRemovable, Helper = static () => Strings.SettingRemovableHelp, Items = [Item(_removable)] },
                new SettingRow { Label = Strings.SettingHealthInterval, Helper = static () => Strings.SettingHealthIntervalHelp, Items = [Item(_interval)] },
                new SettingRow { Label = Strings.SettingTemperature, Helper = static () => Strings.SettingTemperatureHelp, Items = [Item(_temperature)] },
                new SettingRow { Label = Strings.SettingLowSpace, Helper = static () => Strings.SettingLowSpaceHelp, Items = [Item(_lowSpace)] }
            ]);
            AppSettings settings = Services.Settings;
            _history.SelectionChanged += (_, _) => Apply(() => settings.HistoryDays = Pick(AppSettings.HistoryDayChoices, _history), events: true);
            _chkdsk.CheckedChanged += (_, _) => Apply(() => settings.IncludeChkdskEvents = _chkdsk.Checked, events: true);
            _removable.CheckedChanged += (_, _) => Apply(() => settings.ReduceRemovableSeverity = _removable.Checked, events: true);
            _interval.SelectionChanged += (_, _) => Apply(() => settings.HealthIntervalMinutes = Pick(AppSettings.HealthIntervalChoices, _interval), events: false);
            _temperature.SelectionChanged += (_, _) => Apply(() => settings.TemperatureWarningC = Pick(AppSettings.TemperatureChoices, _temperature), events: false);
            _lowSpace.SelectionChanged += (_, _) => Apply(() => settings.LowSpacePercent = Pick(AppSettings.LowSpaceChoices, _lowSpace), events: false);
        }

        private static int Pick(int[] choices, DropDown drop) => choices[Math.Clamp(drop.SelectedIndex, 0, choices.Length - 1)];

        /// <summary>Applies a change, then reloads history (what counts as an event changed) or re-evaluates drives.</summary>
        private void Apply(Action change, bool events)
        {
            if (Loading)
            {
                return;
            }

            change();
            if (events)
            {
                Services.SaveAndReloadEvents();
            }
            else
            {
                Services.SaveAndRefreshDrives();
            }
        }

        public override void Reload() => Load(() =>
        {
            AppSettings settings = Services.Settings;
            _history.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.HistoryDayChoices, settings.HistoryDays));
            _chkdsk.Checked = settings.IncludeChkdskEvents;
            _removable.Checked = settings.ReduceRemovableSeverity;
            _interval.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.HealthIntervalChoices, settings.HealthIntervalMinutes));
            _temperature.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.TemperatureChoices, settings.TemperatureWarningC));
            _lowSpace.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.LowSpaceChoices, settings.LowSpacePercent));
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Updates
    // ---------------------------------------------------------------------------------------------

    private sealed class UpdatesSection : SettingsSectionView
    {
        private readonly UpdateCard _card;

        public UpdatesSection(SettingsPage page) : base(page, "updates", Strings.SectionUpdates, Strings.IntroUpdates)
        {
            _card = new UpdateCard(this);
            Controls.Add(_card);
        }

        public override void Activate()
        {
            base.Activate();
            UpdateService updates = Services.Updates;
            if (updates.LastResult is null && !updates.IsChecking)
            {
                StartCheck();
            }
        }

        public override void OnRefresh(RefreshReason reasons)
        {
            if ((reasons & (RefreshReason.Settings | RefreshReason.Updates)) != 0)
            {
                Reload();
            }
        }

        public override void Reload() => Load(_card.Reload);

        internal void StartCheck() => _card.StartCheck();

        protected override int ArrangeContent(int width, int top)
        {
            int height = _card.Measure();
            _card.SetBounds(0, top, width, height);
            _card.PerformLayout();
            return top + height;
        }

        internal new bool Loading => base.Loading;

        internal void Relayout() => Page.Relayout();
    }

    /// <summary>The update card: version, status line, progress, actions, then the automatic-check row.</summary>
    private sealed class UpdateCard : Card
    {
        private readonly UpdatesSection _section;
        private readonly KitButton _check = new() { Text = Strings.CheckNow, Icon = IconName.Refresh, Backdrop = static t => t.Surface };
        private readonly KitButton _download = new() { Text = Strings.DownloadInstall, Icon = IconName.Download, Variant = ButtonVariant.Primary, Backdrop = static t => t.Surface };
        private readonly KitButton _install = new() { Text = Strings.InstallApp, Backdrop = static t => t.Surface };
        private readonly LinkControl _notes = new() { Text = Strings.ReleaseNotes, Role = FontRole.Body };
        private readonly KitSwitch _auto = new() { AccessibleName = Strings.SettingAutoUpdate, Backdrop = static t => t.Surface };
        private string? _failure;
        private bool _working;

        public UpdateCard(UpdatesSection section)
        {
            _section = section;
            Backdrop = static t => t.Bg;
            Controls.AddRange([_check, _download, _install, _notes, _auto]);
            _check.Click += (_, _) => StartCheck();
            _download.Click += (_, _) => _ = InstallAsync(installer: false);
            _install.Click += (_, _) => ConfirmInstall();
            _notes.Click += (_, _) => Open(_section.Page.AppServices.Updates.LastResult?.ReleaseUrl ?? UpdateService.ReleasesUrl);
            _auto.CheckedChanged += (_, _) =>
            {
                if (!_section.Loading)
                {
                    _section.Page.AppServices.Settings.AutoCheckUpdates = _auto.Checked;
                    _section.Page.AppServices.SaveSettings();
                }
            };
        }

        private UpdateService Updates => _section.Page.AppServices.Updates;

        private bool Downloading => Updates.IsDownloading;

        private int ButtonsTop => S(Downloading ? 90 : 72);

        private int BlockBottom => ButtonsTop + S(32) + S(16);

        public int Measure() => BlockBottom + S(56);

        public void Reload()
        {
            UpdateService updates = Updates;
            UpdateCheckResult? result = updates.LastResult;
            _check.Busy = updates.IsChecking;
            _check.Enabled = !_working && !updates.IsDownloading;
            _download.Visible = result?.CanDownload == true;
            _download.Enabled = !_working && !updates.IsChecking;
            _install.Visible = !AppPaths.IsInstalledBuild && result?.CanInstall == true;
            _install.Enabled = !_working && !updates.IsChecking;
            _auto.Checked = _section.Page.AppServices.Settings.AutoCheckUpdates;
            _section.Relayout();
            PerformLayout();
            Invalidate();
        }

        public void StartCheck()
        {
            if (_section.Page.AppServices.DemoMode || Updates.IsChecking || _working)
            {
                return;
            }

            _failure = null;
            _ = CheckAsync();
        }

        private async Task CheckAsync()
        {
            try
            {
                await Updates.CheckAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
            {
                _failure = Strings.UpdateError(ex.Message);
            }

            if (!IsDisposed)
            {
                Reload();
            }
        }

        private void ConfirmInstall()
        {
            using KitDialog dialog = new KitDialog(Strings.InstallTitle, 480, cancelResult: 0)
                .Paragraph(Strings.InstallText)
                .Button(new DialogButton(Strings.Cancel, ButtonVariant.Secondary, 0, InitialFocus: true))
                .Button(new DialogButton(Strings.InstallConfirm, ButtonVariant.Primary, 1));
            if (dialog.ShowOver(_section.Page.Nav.Window) == 1)
            {
                _ = InstallAsync(installer: true);
            }
        }

        /// <summary>Downloads the signed update (or installer), has it verified and started, then exits for it.</summary>
        private async Task InstallAsync(bool installer)
        {
            if (_working || _section.Page.AppServices.DemoMode)
            {
                return;
            }

            _working = true;
            _failure = null;
            Reload();
            try
            {
                string path = installer ? await Updates.DownloadInstallerAsync() : await Updates.DownloadLatestAsync();
                Updates.InstallDownloadedUpdate(path);
                _section.Page.Nav.ExitForUpdate();
            }
            catch (Exception ex)
            {
                _failure = ex.Message;
                Diagnostics.ErrorLog.Write("Update install", ex);
            }
            finally
            {
                _working = false;
            }

            if (!IsDisposed)
            {
                Reload();
            }
        }

        private static void Open(string address)
        {
            try
            {
                Process.Start(new ProcessStartInfo(address) { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Diagnostics.ErrorLog.Write("Open release notes", ex);
            }
        }

        private string Status()
        {
            UpdateService updates = Updates;
            if (updates.IsDownloading)
            {
                return Strings.UpdateDownloading(updates.DownloadProgress);
            }

            if (updates.IsChecking)
            {
                return Strings.UpdateChecking;
            }

            if (_failure is not null)
            {
                return _failure;
            }

            return updates.LastResult?.Message ?? Strings.UpdateNeverChecked;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = S(16);
            int top = ButtonsTop;
            foreach (KitButton button in new[] { _check, _download, _install })
            {
                if (!button.Visible)
                {
                    continue;
                }

                button.SizeToContent();
                button.Location = new Point(x, top);
                x += button.Width + S(8);
            }

            int linkWidth = Draw.TextWidth(_notes.Text, F(FontRole.Body)) + S(4);
            _notes.SetBounds(x + S(8), top + S(6), linkWidth, S(20));
            _auto.SetBounds(Width - S(16) - S(40), BlockBottom + (S(56) - S(20)) / 2, S(40), S(20));
        }

        protected override void PaintCard(Graphics g)
        {
            ThemeTokens t = T;
            float s = Sc;
            UpdateService updates = Updates;
            Draw.Text(g, Strings.VersionLine(updates.CurrentVersion.ToString()), F(FontRole.BodyStrong), 16 * s, 28 * s, t.Text, Width - S(32));
            Draw.Text(g, Status(), F(FontRole.Body), 16 * s, 50 * s, t.Text2, Width - S(32));
            if (Downloading)
            {
                DataGraphics.ProgressBar(g, new RectangleF(16 * s, 66 * s, Width - 32 * s, 8 * s), updates.DownloadProgress / 100.0, KitAnimator.Now);
            }

            float line = (float)Math.Floor((double)BlockBottom) + 0.5f;
            Draw.Line(g, t.Stroke, s, 16 * s, line, Width - 16 * s, line);
            float rowTop = BlockBottom;
            float content = 20 * s + 16 * s;
            float top = rowTop + (56 * s - content) / 2f;
            int textWidth = Width - S(16 + 16 + 40 + 16);
            Draw.Text(g, Strings.SettingAutoUpdate, F(FontRole.Body), 16 * s, top + 10 * s, t.Text, textWidth);
            Draw.Text(g, Strings.SettingAutoUpdateHelp, F(FontRole.Caption), 16 * s, top + 28 * s, t.Text3, textWidth);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Advanced
    // ---------------------------------------------------------------------------------------------

    private sealed class AdvancedSection : RowsSection
    {
        private readonly KitButton _logs = new() { Text = Strings.OpenLogs };
        private readonly KitButton _data = new() { Text = Strings.OpenData };
        private readonly KitButton _selfCheck = new() { Text = Strings.RunSelfCheck };
        private readonly KitButton _report = new() { Text = Strings.CopySystemReport };

        public AdvancedSection(SettingsPage page) : base(page, "advanced", Strings.SectionAdvanced, Strings.IntroAdvanced)
        {
            SetRows(
            [
                new SettingRow { Label = Strings.SettingLogs, Helper = static () => Strings.SettingLogsHelp, Items = [Item(_logs)] },
                new SettingRow { Label = Strings.SettingData, Helper = static () => Strings.SettingDataHelp, Items = [Item(_data)] },
                new SettingRow { Label = Strings.SettingDiagnostics, Helper = static () => Strings.SettingDiagnosticsHelp, Items = [Item(_selfCheck), Item(_report)] }
            ]);
            _logs.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);
            _data.Click += (_, _) => OpenFolder(AppPaths.DataDirectory);
            _selfCheck.Click += (_, _) => _ = SelfCheckAsync(report: false);
            _report.Click += (_, _) => _ = SelfCheckAsync(report: true);
        }

        public override void Reload()
        {
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diagnostics.ErrorLog.Write("Open folder", ex);
                return;
            }

            OpenWithShell(path);
        }

        /// <summary>Runs <c>--self-check</c> in a child process (its output in a temporary file), then shows or copies it.</summary>
        private async Task SelfCheckAsync(bool report)
        {
            KitButton button = report ? _report : _selfCheck;
            if (_selfCheck.Busy || _report.Busy)
            {
                return;
            }

            button.Busy = true;
            string output = await Task.Run(CaptureSelfCheck);
            if (IsDisposed)
            {
                return;
            }

            button.Busy = false;
            if (report)
            {
                string windows = Environment.OSVersion.VersionString;
                CopyText(Strings.SystemReportHeader(Services.Updates.CurrentVersion.ToString(), windows) + Environment.NewLine + Environment.NewLine + output);
                return;
            }

            ShowOutput(output);
        }

        private static string CaptureSelfCheck()
        {
            string file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                ProcessStartInfo start = new(AppPaths.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--self-check");
                start.ArgumentList.Add("--output");
                start.ArgumentList.Add(file);
                using Process? process = Process.Start(start);
                if (process is null || !process.WaitForExit(120_000))
                {
                    process?.Kill();
                }

                return File.Exists(file) ? File.ReadAllText(file) : "";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                Diagnostics.ErrorLog.Write("Self-check", ex);
                return ex.Message;
            }
            finally
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A leftover temporary file is harmless.
                }
            }
        }

        private void ShowOutput(string output)
        {
            using TextBox text = new()
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = output.ReplaceLineEndings(Environment.NewLine),
                Font = ThemeService.Resources.Font(FontRole.Mono, Dpi)
            };
            text.BackColor = T.Raised;
            text.ForeColor = T.Text;
            ThemeService.ApplyNativeTheme(text);
            using KitDialog dialog = new KitDialog(Strings.SelfCheckTitle, 560, cancelResult: 0)
                .Content(text, 360)
                .Button(new DialogButton(Strings.Copy, ButtonVariant.Secondary, 1))
                .Button(new DialogButton(Strings.ButtonClose, ButtonVariant.Primary, 0, InitialFocus: true));
            if (dialog.ShowOver(Page.Nav.Window) == 1)
            {
                CopyText(output);
            }
        }

        private void CopyText(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
                Page.Nav.ShowToast(new InAppToast(null, Strings.ToastCopied));
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                Diagnostics.ErrorLog.Write("Copy system report", ex);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // About
    // ---------------------------------------------------------------------------------------------

    private sealed class AboutSection : SettingsSectionView
    {
        private readonly AboutCard _card;

        public AboutSection(SettingsPage page) : base(page, "about", Strings.SectionAbout, null)
        {
            _card = new AboutCard(this);
            Controls.Add(_card);
        }

        public override void Reload() => _card.Invalidate();

        protected override int ArrangeContent(int width, int top)
        {
            int height = _card.Measure(width);
            _card.SetBounds(0, top, width, height);
            _card.PerformLayout();
            return top + height;
        }
    }

    private sealed class AboutCard : Card
    {
        private readonly AboutSection _section;
        private readonly LinkControl _website = new() { Text = Strings.LinkWebsite, Role = FontRole.Body };
        private readonly LinkControl _notes = new() { Text = Strings.ReleaseNotes, Role = FontRole.Body };
        private readonly LinkControl _licences = new() { Text = Strings.LinkLicences, Role = FontRole.Body };

        public AboutCard(AboutSection section)
        {
            _section = section;
            Backdrop = static t => t.Bg;
            Controls.AddRange([_website, _notes, _licences]);
            _website.Click += (_, _) => Open(UpdateService.RepositoryUrl);
            _notes.Click += (_, _) => Open(UpdateService.ReleasesUrl);
            _licences.Click += (_, _) =>
            {
                string notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
                if (File.Exists(notices))
                {
                    Open(notices);
                }
            };
        }

        private static void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Diagnostics.ErrorLog.Write("Open link", ex);
            }
        }

        private List<string> PrivacyLines(int width) => Draw.Wrap(Strings.AboutPrivacy, F(FontRole.Caption), Math.Max(S(40), width - S(32)));

        public int Measure(int width) => S(128) + PrivacyLines(width).Count * S(16) + S(16);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = S(16);
            foreach (LinkControl link in new[] { _website, _notes, _licences })
            {
                int width = Draw.TextWidth(link.Text, F(FontRole.Body)) + S(4);
                link.SetBounds(x, S(88), width, S(20));
                x += width + S(16);
            }
        }

        protected override void PaintCard(Graphics g)
        {
            ThemeTokens t = T;
            float s = Sc;
            IconArt.Paint(g, 16 * s, 16 * s, 48 * s);
            Draw.Text(g, Strings.AppName, F(FontRole.Subtitle), 80 * s, 28 * s, t.Text, Width - S(96));
            string version = _section.Page.AppServices.Updates.CurrentVersion.ToString();
            Draw.Text(g, Strings.VersionLine(version), F(FontRole.Body), 80 * s, 52 * s, t.Text2, Width - S(96));
            string kind = (AppPaths.IsInstalledBuild ? Strings.AboutInstalled : Strings.AboutPortable) + Strings.Separator
                + (Elevation.IsElevated ? Strings.AboutAdmin : Strings.AboutStandard);
            Draw.Text(g, kind, F(FontRole.Caption), 80 * s, 72 * s, t.Text3, Width - S(96));
            List<string> lines = PrivacyLines(Width);
            for (int i = 0; i < lines.Count; i++)
            {
                Draw.Text(g, lines[i], F(FontRole.Caption), 16 * s, (128 + 8 + 16 * i) * s, t.Text2);
            }
        }
    }
}
