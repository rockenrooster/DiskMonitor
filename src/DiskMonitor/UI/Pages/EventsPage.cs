using System.Globalization;
using System.Text;
using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>One row of the Events list: a single event, or a recurring problem (one group key) combined.</summary>
internal sealed class EventGroup : ListRow
{
    public EventGroup(string key, List<DiskEvent> events, int unread)
    {
        GroupId = key;
        Events = events;
        UnreadCount = unread;
    }

    public string GroupId { get; }

    public List<DiskEvent> Events { get; }

    public DiskEvent Latest => Events[0];

    public int Count => Events.Count;

    public int UnreadCount { get; }

    public DateTime FirstUtc => Events[^1].TimeUtc;

    public override object Key => GroupId;

    public override int LogicalHeight => 44;

    public override string AccessibleName =>
        $"{Strings.TierName(Latest.Tier)}, {Latest.Title}, {Latest.DeviceText}, {Format.RecentDate(Latest.TimeUtc, inSentence: false)}";
}

internal sealed class GroupHeaderRow(string id, string label, int count, Tier? tier, bool drive, bool collapsed) : ListRow
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public int Count { get; } = count;
    public Tier? Tier { get; } = tier;
    public bool DriveGroup { get; } = drive;
    public bool Collapsed { get; } = collapsed;

    public override object Key => "group:" + Id;

    public override int LogicalHeight => 32;

    public override bool Selectable => false;

    public override string AccessibleName => $"{Label} {Count}";
}

internal sealed class SummaryRow(int shown, int total) : ListRow
{
    public int Shown { get; } = shown;
    public int Total { get; } = total;

    public override object Key => "summary";

    public override int LogicalHeight => 32;

    public override bool Selectable => false;

    public override string AccessibleName => Strings.ShowingOf(Shown, Total);
}

/// <summary>
/// Events (subplan 05): "What went wrong, and how bad is it?" — the full history, fast to narrow down,
/// with a plain-language account of every event and the next step beside it.
/// </summary>
internal sealed class EventsPage : PageBase
{
    private readonly KitButton _refresh = new() { Icon = IconName.Refresh, Variant = ButtonVariant.Subtle, ToolTipText = Strings.EventsRefreshTip, AccessibleName = Strings.EventsRefreshTip };
    private readonly KitButton _export = new() { Text = Strings.EventsExport, Icon = IconName.Report };
    private readonly KitButton _markAll = new() { Text = Strings.EventsMarkAllRead, Icon = IconName.Check, DisabledReason = Strings.EventsNothingUnread };
    private readonly Chip _all = new() { Text = Strings.ChipAll, AutoToggle = false };
    private readonly Chip[] _tierChips;
    private readonly Chip[] _kindChips;
    private readonly DropDown _drive = new() { Compact = true };
    private readonly DropDown _show = new() { Compact = true };
    private readonly DropDown _range = new() { Compact = true };
    private readonly DropDown _repeats = new() { Compact = true };
    private readonly DropDown _kindDrop = new() { Compact = true, Visible = false };
    private readonly DropDown _groupDrop = new() { Compact = true, Visible = false };
    private readonly SegmentedControl _group = new() { Compact = true };
    private readonly ListFrame _frame;
    private readonly VirtualList _list = new();
    private readonly SearchBox _search = new() { Placeholder = Strings.SearchEvents, Visible = false, Inset = true };
    private readonly EmptyStateControl _empty = new() { Visible = false };
    private readonly SelectionBar _selection;
    private readonly EventDrawer _drawer = new();
    private readonly HashSet<Tier> _tiers = [];
    private readonly HashSet<ErrorKind> _kinds = [];
    private readonly HashSet<string> _collapsed = [];
    private readonly List<int?> _driveNumbers = [null];
    private readonly Dictionary<Tier, int> _tierCounts = [];
    private IReadOnlyList<ListRow> _rows = [];
    private List<EventGroup> _groups = [];
    private int _total;
    private bool _unreadOnly;
    private int? _disk;
    private string _searchText = "";
    private int _pending;
    private List<ListRow>? _pendingRows;
    private bool _narrow;

    public EventsPage(AppServices services, INavigator navigator) : base(services, navigator)
    {
        _tierChips = TierExtensions.Descending.Select(tier => new Chip { Text = Strings.TierName(tier), Severity = tier.ToLevel(), AutoToggle = false }).ToArray();
        _kindChips = TierExtensions.Kinds.Select(kind => new Chip { Text = Strings.KindName(kind), AutoToggle = false }).ToArray();
        _frame = new ListFrame(this);
        _selection = new SelectionBar(this);

        _refresh.Click += (_, _) => _ = Services.Monitor.ReloadAsync();
        _export.Click += (_, _) => Export(FilteredEvents());
        _markAll.Click += (_, _) => MarkAllRead();
        _all.Toggled += (_, _) => { _tiers.Clear(); Apply(); };
        for (int i = 0; i < _tierChips.Length; i++)
        {
            Tier tier = TierExtensions.Descending[i];
            _tierChips[i].Toggled += (_, _) =>
            {
                if (!_tiers.Remove(tier))
                {
                    _tiers.Add(tier);
                }

                Apply();
            };
        }

        for (int i = 0; i < _kindChips.Length; i++)
        {
            ErrorKind kind = TierExtensions.Kinds[i];
            _kindChips[i].Toggled += (_, _) =>
            {
                if (!_kinds.Remove(kind))
                {
                    _kinds.Add(kind);
                }

                Apply();
            };
        }

        _show.SetItems([Strings.ShowAll, Strings.ShowUnread]);
        _range.SetItems([Strings.Range24, Strings.Range7, Strings.Range30, Strings.RangeAll], 3);
        _range.DefaultIndex = 3;
        _repeats.SetItems([Strings.RepeatsCombine, Strings.RepeatsEach], services.Settings.GroupSimilar ? 0 : 1);
        _group.SetItems([Strings.GroupTier, Strings.GroupDrive, Strings.GroupNone], Array.IndexOf(AppSettings.EventsGroupByChoices, services.Settings.EventsGroupBy));
        _groupDrop.SetItems([Strings.GroupTier, Strings.GroupDrive, Strings.GroupNone], _group.SelectedIndex);
        _groupDrop.LabelFormat = Strings.GroupLabel;
        _kindDrop.SetItems([Strings.KindAll, .. TierExtensions.Kinds.Select(Strings.KindName)]);
        _drive.SelectionChanged += (_, _) => { _disk = _driveNumbers[Math.Clamp(_drive.SelectedIndex, 0, _driveNumbers.Count - 1)]; Apply(); };
        _show.SelectionChanged += (_, _) => { _unreadOnly = _show.SelectedIndex == 1; Apply(); };
        _range.SelectionChanged += (_, _) => Apply();
        _repeats.SelectionChanged += (_, _) =>
        {
            Services.Settings.GroupSimilar = _repeats.SelectedIndex == 0;
            Services.SaveSettings();
        };
        _group.SelectionChanged += (_, _) => SetGrouping(_group.SelectedIndex);
        _groupDrop.SelectionChanged += (_, _) => SetGrouping(_groupDrop.SelectedIndex);
        _kindDrop.SelectionChanged += (_, _) =>
        {
            _kinds.Clear();
            if (_kindDrop.SelectedIndex > 0)
            {
                _kinds.Add(TierExtensions.Kinds[_kindDrop.SelectedIndex - 1]);
            }

            Apply();
        };

        _list.RowPainter = PaintRow;
        _list.RowHitTest = HitTestRow;
        _list.RowActivated += (_, row) => Activate(row);
        _list.RowAction += (_, action) => OnRowAction(action.Row, action.Action);
        _list.SelectionChanged += (_, _) => _selection.Refresh(_list.SelectedRows.OfType<EventGroup>().ToList());
        _list.ContextMenuRequested += (_, request) => { if (request.Row is EventGroup group) { ShowMenu(group, request.Screen); } };
        _list.EscapePressed += (_, _) => _ = HandleEscape();
        _list.FocusMoved += (_, _) =>
        {
            if (_drawer.Visible && _list.FocusedRow is EventGroup group)
            {
                OpenDrawer(group);
            }
        };
        _list.MouseLeave += (_, _) => ApplyPendingIfFree();
        _search.SearchChanged += (_, _) => { _searchText = _search.Text.Trim(); Apply(); };
        _search.Dismissed += (_, _) => CloseSearch();
        _empty.ButtonClicked += (_, _) => ClearFilters();

        _drawer.LogicalWidth = services.Settings.DrawerWidth;
        _drawer.WidthCommitted += (_, width) => { Services.Settings.DrawerWidth = width; Services.SaveSettings(); };
        _drawer.CloseRequested += (_, _) => CloseDrawer();
        _drawer.PrimaryRequested += (_, group) => RunPrimary(group);
        _drawer.MoreRequested += (_, request) => ShowMenu(request.Group, request.Screen);
        _drawer.CopyRequested += (_, group) => CopyGroups([group]);
        _drawer.IgnoreRequested += (_, group) => Ignore(group);
        _drawer.TierMenuRequested += (_, request) => KitMenu.ShowAt(TierMenu(request.Group), request.Screen, Dpi, Navigator.Window);
        _drawer.DriveRequested += (_, disk) => Navigator.ShowDrive(disk);

        foreach (Control control in new Control[] { _all }.Concat(_tierChips).Concat(_kindChips).Concat([_drive, _show, _range, _repeats, _kindDrop, _group, _groupDrop, _frame]))
        {
            Controls.Add(control);
        }

        _frame.Controls.Add(_list);
        _frame.Controls.Add(_search);
        _frame.Controls.Add(_empty);
        _frame.Controls.Add(_selection);
        _empty.Backdrop = static t => t.Surface;
        _search.Backdrop = static t => t.Surface;
        _selection.Visible = false;
    }

    public override string Key => "Events";

    public override string Title => Strings.PageEvents;

    public override IReadOnlyList<Control> Toolbar => [_refresh, _export, _markAll];

    public override void RefreshData(RefreshReason reasons)
    {
        if ((reasons & (RefreshReason.Events | RefreshReason.Settings | RefreshReason.Drives | RefreshReason.Monitor)) != 0)
        {
            Apply(fromData: true);
        }
    }

    public override bool FocusSearch()
    {
        _search.Visible = true;
        _frame.PerformLayout();
        _search.Focus();
        return true;
    }

    public override bool HandleEscape()
    {
        if (_drawer.Visible)
        {
            CloseDrawer();
            return true;
        }

        if (_list.SelectedKeys.Count > 0)
        {
            _list.ClearSelection();
            return true;
        }

        return false;
    }

    public override void Navigate(object argument)
    {
        switch (argument)
        {
            case EventKey key:
                ShowEvent(key.Key);
                break;
            case EventFilter filter:
                ClearFilters(apply: false);
                if (filter.Tiers is { } tiers)
                {
                    _tiers.UnionWith(tiers);
                }

                _disk = filter.DiskNumber;
                _unreadOnly = filter.UnreadOnly;
                _show.SelectedIndex = filter.UnreadOnly ? 1 : 0;
                CloseDrawer();
                Apply();
                _drive.SelectedIndex = Math.Max(0, _driveNumbers.IndexOf(_disk));
                break;
        }
    }

    public override void Deactivate() => CloseDrawer(animate: false);

    public override int Arrange(int width, int viewportHeight)
    {
        int s28 = S(28);
        _narrow = width < S(760);
        int x = 0;
        foreach (Chip chip in new[] { _all }.Concat(_tierChips))
        {
            chip.SetBounds(x, 0, chip.PreferredWidth, s28);
            x += chip.Width + S(8);
        }

        _frame.DividerX = _narrow ? -1 : x;
        x += S(9) + S(8);
        foreach (Chip chip in _kindChips)
        {
            chip.Visible = !_narrow;
            chip.SetBounds(x, 0, chip.PreferredWidth, s28);
            x += chip.Width + S(8);
        }

        int y = S(40);
        x = 0;
        DropDown[] drops = _narrow ? [_drive, _show, _range, _repeats, _kindDrop] : [_drive, _show, _range, _repeats];
        _kindDrop.Visible = _narrow;
        foreach (DropDown drop in drops)
        {
            drop.SetBounds(x, y, drop.PreferredWidth(S(96)), s28);
            x += drop.Width + S(8);
        }

        _group.Visible = !_narrow;
        _groupDrop.Visible = _narrow;
        if (_narrow)
        {
            // Unspecified design case: when the drop-downs fill the second row, the grouping drop-down
            // moves to the right end of the first row, which the kind chips have left.
            int groupWidth = _groupDrop.PreferredWidth(S(96));
            int groupY = x + groupWidth > width ? 0 : y;
            _groupDrop.SetBounds(width - groupWidth, groupY, groupWidth, s28);
        }
        else
        {
            _group.SetBounds(width - _group.PreferredWidth, y, _group.PreferredWidth, s28);
        }

        int listTop = S(80);
        _frame.SetBounds(0, listTop, width, Math.Max(S(200), viewportHeight - listTop));
        _frame.PerformLayout();
        return Math.Max(viewportHeight, listTop + S(200));
    }

    /// <summary>Paints the gap the frame leaves for the "Group by" caption and the chip divider.</summary>
    protected override void PaintContent(Graphics g)
    {
        if (!_narrow && _group.Visible)
        {
            Draw.TextRight(g, Strings.GroupBy, F(FontRole.Body), _group.Left - Sf(8), _group.Top + _group.Height / 2f, T.Text2);
        }

        if (_frame.DividerX > 0)
        {
            float x = (float)Math.Floor(_frame.DividerX + Sf(8)) + Sf(0.5f);
            Draw.Line(g, T.Stroke, Sc, x, Sf(4), x, Sf(24));
        }
    }

    // --- pipeline -------------------------------------------------------------------------------------

    private void SetGrouping(int index)
    {
        _group.SelectedIndex = index;
        _groupDrop.SelectedIndex = index;
        Services.Settings.EventsGroupBy = AppSettings.EventsGroupByChoices[Math.Clamp(index, 0, 2)];
        Services.SaveSettings();
        Apply();
    }

    private bool Matches(DiskEvent item, DateTime now, string search)
    {
        TimeSpan? range = _range.SelectedIndex switch
        {
            0 => TimeSpan.FromHours(24),
            1 => TimeSpan.FromDays(7),
            2 => TimeSpan.FromDays(30),
            _ => null
        };
        if (range is { } window && now - item.TimeUtc > window)
        {
            return false;
        }

        if (_disk is { } disk && item.DiskNumber != disk)
        {
            return false;
        }

        if (_kinds.Count > 0 && !_kinds.Contains(item.Kind))
        {
            return false;
        }

        if (_unreadOnly && !Services.Events.IsUnread(item))
        {
            return false;
        }

        if (search.Length > 0
            && !item.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            && !item.Message.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            && !item.DeviceText.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            && !item.Provider.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            && !item.RuleId.Contains(search, StringComparison.OrdinalIgnoreCase)
            && !item.EventId.ToString(CultureInfo.InvariantCulture).Equals(search, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    /// <summary>Events that pass steps 1 to 4 of the pipeline (range, drive, kind, show, search, tiers).</summary>
    private List<DiskEvent> FilteredEvents()
    {
        DateTime now = Clock.UtcNow;
        List<DiskEvent> result = [];
        foreach (DiskEvent item in Services.Events.Snapshot())
        {
            if (Matches(item, now, _searchText) && (_tiers.Count == 0 || _tiers.Contains(item.Tier)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private void Apply(bool fromData = false)
    {
        IReadOnlyList<DiskEvent> all = Services.Events.Snapshot();
        DateTime now = Clock.UtcNow;
        _total = all.Count;
        UpdateDriveChoices(all);

        // Steps 2-4: the other filters, counts per tier from the same predicate, then the tier chips.
        foreach (Tier tier in TierExtensions.Descending)
        {
            _tierCounts[tier] = 0;
        }

        List<DiskEvent> passing = [];
        int baseCount = 0;
        foreach (DiskEvent item in all)
        {
            if (!Matches(item, now, _searchText))
            {
                continue;
            }

            baseCount++;
            _tierCounts[item.Tier]++;
            if (_tiers.Count == 0 || _tiers.Contains(item.Tier))
            {
                passing.Add(item);
            }
        }

        _all.On = _tiers.Count == 0;
        _all.Count = Format.Number(baseCount);
        for (int i = 0; i < _tierChips.Length; i++)
        {
            Tier tier = TierExtensions.Descending[i];
            _tierChips[i].On = _tiers.Contains(tier);
            _tierChips[i].Count = Format.Number(_tierCounts[tier]);
        }

        for (int i = 0; i < _kindChips.Length; i++)
        {
            _kindChips[i].On = _kinds.Contains(TierExtensions.Kinds[i]);
        }

        // Step 5: combine repeats.
        List<EventGroup> groups = [];
        if (Services.Settings.GroupSimilar)
        {
            Dictionary<string, List<DiskEvent>> byKey = [];
            List<string> order = [];
            foreach (DiskEvent item in passing)
            {
                if (!byKey.TryGetValue(item.GroupKey, out List<DiskEvent>? list))
                {
                    list = [];
                    byKey[item.GroupKey] = list;
                    order.Add(item.GroupKey);
                }

                list.Add(item);
            }

            foreach (string key in order)
            {
                List<DiskEvent> list = byKey[key];
                groups.Add(new EventGroup(key, list, list.Count(Services.Events.IsUnread)));
            }
        }
        else
        {
            foreach (DiskEvent item in passing)
            {
                groups.Add(new EventGroup(item.Key, [item], Services.Events.IsUnread(item) ? 1 : 0));
            }
        }

        // Step 6: group by tier, drive, or none.
        List<ListRow> rows = [];
        bool filtered = _tiers.Count > 0 || _kinds.Count > 0 || _disk is not null || _unreadOnly || _range.SelectedIndex != 3 || _searchText.Length > 0;
        if (filtered && !_search.Visible)
        {
            rows.Add(new SummaryRow(passing.Count, _total));
        }

        string grouping = Services.Settings.EventsGroupBy;
        if (grouping == "Tier")
        {
            foreach (Tier tier in TierExtensions.Descending)
            {
                List<EventGroup> members = groups.Where(g => g.Latest.Tier == tier).ToList();
                AddSection(rows, "tier:" + tier, Strings.TierName(tier), members, tier, drive: false);
            }
        }
        else if (grouping == "Drive")
        {
            DriveSnapshot drives = Services.Drives.Current;
            foreach (IGrouping<int?, EventGroup> section in groups.GroupBy(g => g.Latest.DiskNumber).OrderBy(s => s.Key is null ? int.MaxValue : s.Key.Value))
            {
                string label = section.Key is { } number ? DriveLabel(number, drives) : Strings.NoDrive;
                AddSection(rows, "disk:" + (section.Key?.ToString(CultureInfo.InvariantCulture) ?? "none"), label, section.ToList(), null, drive: true);
            }
        }
        else
        {
            rows.AddRange(groups);
        }

        _groups = groups;
        bool frozen = fromData && _rows.Count > 0 && (_list.ClientRectangle.Contains(_list.PointToClient(MousePosition)) && _list.Visible
            || _drawer.Visible || _list.FocusIndex > 0);
        if (frozen)
        {
            HashSet<object> shown = _rows.Select(r => r.Key).ToHashSet();
            _pending = rows.OfType<EventGroup>().Count(r => !shown.Contains(r.Key));
            if (_pending > 0)
            {
                _pendingRows = rows;
                _frame.PendingCount = _pending;
                UpdateChrome(passing.Count);
                return;
            }
        }

        _pending = 0;
        _pendingRows = null;
        _frame.PendingCount = 0;
        _rows = rows;
        _list.SetRows(rows);
        UpdateChrome(passing.Count);

        if (_drawer.Visible && _drawer.Group is { } open)
        {
            EventGroup? updated = groups.FirstOrDefault(g => g.GroupId == open.GroupId);
            if (updated is null)
            {
                CloseDrawer();
            }
            else
            {
                _drawer.Show(updated, Services.Drives.Current);
            }
        }
    }

    private void AddSection(List<ListRow> rows, string id, string label, List<EventGroup> members, Tier? tier, bool drive)
    {
        if (members.Count == 0)
        {
            return;
        }

        bool collapsed = _collapsed.Contains(id);
        rows.Add(new GroupHeaderRow(id, label, members.Sum(m => m.Count), tier, drive, collapsed));
        if (!collapsed)
        {
            rows.AddRange(members);
        }
    }

    private static string DriveLabel(int number, DriveSnapshot drives)
    {
        PhysicalDisk? disk = drives.FindDisk(number);
        return disk is null ? Strings.DeviceDiskMissing(number) : Strings.DropDisk(number, disk.DisplayName);
    }

    private void UpdateDriveChoices(IReadOnlyList<DiskEvent> all)
    {
        SortedSet<int> numbers = [];
        foreach (DiskEvent item in all)
        {
            if (item.DiskNumber is { } number)
            {
                numbers.Add(number);
            }
        }

        List<int?> wanted = [null, .. numbers.Select(n => (int?)n)];
        if (wanted.SequenceEqual(_driveNumbers))
        {
            return;
        }

        _driveNumbers.Clear();
        _driveNumbers.AddRange(wanted);
        DriveSnapshot drives = Services.Drives.Current;
        _drive.SetItems([Strings.DropAllDrives, .. numbers.Select(n => DriveLabel(n, drives))], Math.Max(0, _driveNumbers.IndexOf(_disk)));
    }

    private void UpdateChrome(int shown)
    {
        _markAll.Enabled = Services.Events.GetUnread().Total > 0;
        bool empty = _rows.Count == 0 || _rows.All(r => r is SummaryRow);
        bool loading = Services.Monitor.IsLoading && _total == 0;
        _list.Loading = loading;
        _empty.Visible = empty && !loading;
        if (_empty.Visible)
        {
            bool filtered = _total > 0;
            _empty.Icon = filtered ? IconName.Filter : IconName.Check;
            _empty.Headline = filtered ? Strings.EmptyFilteredTitle : Strings.EmptyNoEventsTitle;
            _empty.Message = filtered ? null : Strings.EmptyNoEventsText(Services.Settings.HistoryDays);
            _empty.ButtonText = filtered ? Strings.ClearFilters : null;
            _empty.Invalidate();
        }

        _frame.PerformLayout();
        _frame.Invalidate();
        Invalidate();
    }

    private void ApplyPendingIfFree()
    {
        if (_pendingRows is not null && !_drawer.Visible && _list.FocusIndex <= 0)
        {
            ApplyPending();
        }
    }

    internal void ApplyPending()
    {
        if (_pendingRows is null)
        {
            return;
        }

        _rows = _pendingRows;
        _pendingRows = null;
        _pending = 0;
        _frame.PendingCount = 0;
        _list.SetRows(_rows);
        UpdateChrome(_rows.OfType<EventGroup>().Sum(g => g.Count));
    }

    internal void ClearFilters(bool apply = true)
    {
        _tiers.Clear();
        _kinds.Clear();
        _disk = null;
        _unreadOnly = false;
        _drive.SelectedIndex = 0;
        _show.SelectedIndex = 0;
        _range.SelectedIndex = 3;
        _kindDrop.SelectedIndex = 0;
        _searchText = "";
        if (_search.Visible)
        {
            _search.Inner.Text = "";
        }

        if (apply)
        {
            Apply();
        }
    }

    private void CloseSearch()
    {
        _search.Visible = false;
        _searchText = "";
        Apply();
        _list.Focus();
    }

    // --- rows -------------------------------------------------------------------------------------------

    private void PaintRow(Graphics g, ListRow row, Rectangle r, RowState state)
    {
        ThemeTokens t = T;
        float s = Sc;
        switch (row)
        {
            case SummaryRow summary:
            {
                Font body = F(FontRole.Body);
                string text = Strings.ShowingOf(summary.Shown, summary.Total);
                Draw.Text(g, text, body, r.X + 16 * s, r.Y + r.Height / 2f, t.Text2);
                float linkX = r.X + 16 * s + Draw.TextWidth(text, body) + 12 * s;
                Draw.Text(g, Strings.ClearFilters, F(FontRole.BodyStrong), linkX, r.Y + r.Height / 2f, t.Accent);
                float underline = (float)Math.Floor(r.Y + r.Height / 2f + 9 * s) + 0.5f;
                Draw.Line(g, t.Accent, s, linkX, underline, linkX + Draw.TextWidth(Strings.ClearFilters, F(FontRole.BodyStrong)), underline);
                RowPaint.Divider(g, r.X, r.Right, r.Bottom, Dpi);
                break;
            }

            case GroupHeaderRow header:
                RowPaint.GroupHeader(g, r, header.Collapsed, header.Tier?.ToLevel(), header.DriveGroup ? IconName.Drive : null, header.Label, Format.Number(header.Count), Dpi, state.Focus);
                break;

            case EventGroup group:
                PaintEventRow(g, group, r, state);
                break;
        }
    }

    private void PaintEventRow(Graphics g, EventGroup group, Rectangle r, RowState state)
    {
        ThemeTokens t = T;
        float s = Sc;
        DiskEvent item = group.Latest;
        bool selected = state.Selected || (_drawer.Visible && _drawer.Group?.GroupId == group.GroupId);
        RowPaint.Background(g, r, state.Hover, selected, 32 * s, Dpi);
        Color behind = selected ? t.Selected : state.Hover ? t.Hover : t.Surface;
        bool contrastSelected = selected && t.IsHighContrast;
        Color text = contrastSelected ? t.OnSelected : t.Text;
        Color text2 = contrastSelected ? t.OnSelected : t.Text2;
        if (group.UnreadCount > 0)
        {
            Draw.Circle(g, contrastSelected ? t.OnSelected : t.Accent, r.X + 6 * s, r.Y + 22 * s, 3 * s);
        }

        Draw.Severity(g, item.Tier.ToLevel(), r.X + 16 * s, r.Y + 14 * s, 16, Dpi, contrastSelected ? t.OnSelected : t.TierText(item.Tier), behind);
        bool actions = state.Hover || state.Focus;
        float right = r.Right - 16 * s;
        Font body = F(FontRole.Body);
        if (actions)
        {
            (Rectangle button, Rectangle more, string label) = ActionRects(group, r);
            KitButton.Render(g, button, ButtonVariant.Secondary, label, null, false, default, Dpi);
            KitButton.Render(g, more, ButtonVariant.Subtle, "", IconName.More, false, default, Dpi);
            right = button.X - 8 * s;
        }
        else
        {
            string time = Format.RecentDate(item.TimeUtc, inSentence: false);
            Draw.TextRight(g, time, body, right, r.Y + 22 * s, text2);
            right -= Draw.TextWidth(time, body) + 12 * s;
        }

        if (group.Count > 1)
        {
            // 100 px from the right (132 more while the actions show), or left of a longer time.
            float countRight = Math.Min(r.Right - (actions ? 132 + 100 : 100) * s, right);
            string count = Strings.RowCount(group.Count);
            Draw.TextRight(g, count, body, countRight, r.Y + 22 * s, contrastSelected ? t.OnSelected : t.Text3);
            right = Math.Min(right, countRight - Draw.TextWidth(count, body) - 12 * s);
        }

        int width = (int)Math.Max(1, right - r.X - 44 * s - 8 * s);
        Draw.Text(g, item.Title, F(FontRole.BodyStrong), r.X + 44 * s, r.Y + 13 * s, text, width);
        Draw.Text(g, SecondLine(item), body, r.X + 44 * s, r.Y + 31 * s, text2, width);
        RowPaint.Divider(g, r.X + 44 * s, r.Right, r.Bottom, Dpi);
    }

    private (Rectangle Button, Rectangle More, string Label) ActionRects(EventGroup group, Rectangle r)
    {
        string label = group.Latest.SuggestScan && group.Latest.Volume is { } volume ? Strings.ActionScanVolume(volume) : Strings.RowDetails;
        int width = KitButton.MeasureWidth(label, false, false, ButtonVariant.Secondary, true, Dpi);
        Rectangle more = new(r.Right - S(16) - S(28), r.Y + S(8), S(28), S(28));
        Rectangle button = new(more.X - S(4) - width, r.Y + S(8), width, S(28));
        return (button, more, label);
    }

    private static string SecondLine(DiskEvent item)
    {
        if (item.Synthetic)
        {
            return Strings.RowHealthCheck(item.DeviceText);
        }

        string first = item.DeviceText.Length > 0 ? item.DeviceText : Strings.KindName(item.Kind);
        return Strings.RowDevice(first, item.Provider, item.EventId);
    }

    private string? HitTestRow(ListRow row, Point point, Rectangle bounds)
    {
        if (row is SummaryRow)
        {
            return "clear";
        }

        if (row is GroupHeaderRow)
        {
            return "toggle";
        }

        if (row is EventGroup group)
        {
            Rectangle local = new(0, 0, bounds.Width, bounds.Height);
            (Rectangle button, Rectangle more, _) = ActionRects(group, local);
            if (button.Contains(point))
            {
                return "primary";
            }

            if (more.Contains(point))
            {
                return "more";
            }
        }

        return null;
    }

    private void OnRowAction(ListRow row, string action)
    {
        switch (row, action)
        {
            case (SummaryRow, "clear"):
                ClearFilters();
                break;
            case (GroupHeaderRow header, "toggle"):
                if (!_collapsed.Remove(header.Id))
                {
                    _collapsed.Add(header.Id);
                }

                Apply();
                break;
            case (EventGroup group, "primary"):
                if (group.Latest.SuggestScan && group.Latest.Volume is { } volume)
                {
                    Navigator.ShowCheckDisk(volume, new ScanOfferReason(group.Latest.TimeUtc));
                }
                else
                {
                    OpenDrawer(group);
                }

                break;
            case (EventGroup group, "more"):
                Rectangle bounds = _list.RowBounds(_list.IndexOfKey(group.Key));
                (_, Rectangle more, _) = ActionRects(group, bounds);
                ShowMenu(group, _list.PointToScreen(new Point(more.Left, more.Bottom)));
                break;
        }
    }

    private void Activate(ListRow row)
    {
        switch (row)
        {
            case EventGroup group:
                OpenDrawer(group);
                break;
            case GroupHeaderRow header:
                OnRowAction(header, "toggle");
                break;
            case SummaryRow summary:
                OnRowAction(summary, "clear");
                break;
        }
    }

    // --- drawer, read state ---------------------------------------------------------------------------

    private void ShowEvent(string key)
    {
        ClearFilters(apply: false);
        _collapsed.Clear();
        Apply();
        EventGroup? group = _groups.FirstOrDefault(g => g.Events.Any(e => e.Key == key));
        if (group is null)
        {
            _list.ClearSelection();
            CloseDrawer();
            return;
        }

        int index = _list.IndexOfKey(group.Key);
        _list.SelectOnly(index);
        OpenDrawer(group);
    }

    private void OpenDrawer(EventGroup group)
    {
        Services.Events.MarkRead(group.Events);
        _drawer.Show(group, Services.Drives.Current);
        Navigator.ShowDrawer(_drawer);
        _list.Invalidate();
    }

    private void CloseDrawer(bool animate = true)
    {
        if (_drawer.Visible)
        {
            if (animate)
            {
                Navigator.HideDrawer();
            }
            else
            {
                _drawer.Visible = false;
            }

            _list.Invalidate();
            ApplyPendingIfFree();
        }
    }

    private void RunPrimary(EventGroup group)
    {
        DiskEvent item = group.Latest;
        string? label = EventDrawer.PrimaryLabel(item, Services.Drives.Current);
        if (label == Strings.RecOpenDrive && item.DiskNumber is { } disk)
        {
            Navigator.ShowDrive(disk);
        }
        else if (item.Volume is { } volume)
        {
            Navigator.ShowCheckDisk(volume, new ScanOfferReason(item.TimeUtc));
        }
    }

    private void MarkAllRead()
    {
        int count = Services.Events.GetUnread().Total;
        ReadState before = Services.Events.CaptureReadState();
        Services.Events.MarkAllRead();
        void Undo() => Services.Events.RestoreReadState(before);
        Navigator.ShowToast(new InAppToast(Level.Ok, Strings.ToastMarkedRead(count), null, Strings.Undo, Undo));
        Navigator.SetUndo(Undo);
    }

    // --- menus, rules, copy, export -------------------------------------------------------------------

    private void ShowMenu(EventGroup group, Point screen)
    {
        DiskEvent item = group.Latest;
        string? primary = EventDrawer.PrimaryLabel(item, Services.Drives.Current);
        List<MenuEntry> entries =
        [
            new(Strings.RowDetails, IconName.Info, Strings.ShortcutEnter, () => { _list.SelectOnly(_list.IndexOfKey(group.Key)); OpenDrawer(group); })
        ];
        if (primary is not null)
        {
            entries.Add(new MenuEntry(primary, primary == Strings.RecOpenDrive ? IconName.Drive : IconName.Play, Strings.ShortcutCtrlEnter, () => RunPrimary(group)));
        }

        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry(Strings.MarkAsRead, IconName.Check, null, () => Services.Events.MarkRead(group.Events)) { Enabled = group.UnreadCount > 0 });
        entries.Add(new MenuEntry(Strings.IgnoreThisKind, IconName.Ignore, Strings.ShortcutCtrlI, () => Ignore(group)));
        entries.Add(new MenuEntry(Strings.ChangeTier) { Submenu = TierMenu(group) });
        entries.Add(MenuEntry.Separator);
        if (item.DiskNumber is { } disk)
        {
            entries.Add(new MenuEntry(Strings.ShowOnlyThisDrive, IconName.Filter, null, () => Navigate(new EventFilter(DiskNumber: disk))));
        }

        entries.Add(new MenuEntry(Strings.CopyDetails, IconName.Copy, Strings.ShortcutCtrlC, () => CopyGroups([group])));
        if (!item.Synthetic)
        {
            entries.Add(new MenuEntry(Strings.OpenInEventViewer, IconName.Open, null, () => EventViewer.Open(item)));
        }

        KitMenu.ShowAt(entries, screen, Dpi, Navigator.Window);
    }

    private List<MenuEntry> TierMenu(EventGroup group)
    {
        DiskEvent item = group.Latest;
        EventRule? rule = RuleCatalog.Find(item.RuleId);
        Tier defaultTier = rule?.Tier ?? item.Tier;
        List<MenuEntry> entries = [];
        foreach (Tier tier in TierExtensions.Descending)
        {
            entries.Add(new MenuEntry(Strings.TierName(tier), null, null, () => ChangeTier(group, tier)) { Checked = item.Tier == tier });
        }

        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry(Strings.UseDefaultTier(defaultTier), null, null, () => ChangeTier(group, null)));
        return entries;
    }

    private void Ignore(EventGroup group)
    {
        string rule = group.Latest.RuleId;
        int hidden = Services.Events.Snapshot().Count(e => e.RuleId.Equals(rule, StringComparison.OrdinalIgnoreCase));
        string? previous = Services.Settings.RuleOverrides.GetValueOrDefault(rule);
        Services.SetRuleOverride(rule, RuleCatalog.IgnoreValue);
        CloseDrawer();
        void Undo() => Services.SetRuleOverride(rule, previous);
        Navigator.ShowToast(new InAppToast(Level.Ok, Strings.ToastIgnored(group.Latest.Title), Strings.ToastIgnoredText(hidden), Strings.Undo, Undo));
        Navigator.SetUndo(Undo);
    }

    private void ChangeTier(EventGroup group, Tier? tier)
    {
        string rule = group.Latest.RuleId;
        int changed = Services.Events.Snapshot().Count(e => e.RuleId.Equals(rule, StringComparison.OrdinalIgnoreCase));
        string? previous = Services.Settings.RuleOverrides.GetValueOrDefault(rule);
        EventRule? catalog = RuleCatalog.Find(rule);
        string? value = tier is null || (catalog is not null && catalog.Tier == tier && !catalog.Ignore) ? null : tier.ToString();
        Services.SetRuleOverride(rule, value);
        void Undo() => Services.SetRuleOverride(rule, previous);
        string title = tier is { } shown ? Strings.ToastMoved(group.Latest.Title, shown) : Strings.ToastMovedDefault(group.Latest.Title);
        Navigator.ShowToast(new InAppToast(Level.Ok, title, Strings.ToastMovedText(changed), Strings.Undo, Undo));
        Navigator.SetUndo(Undo);
    }

    internal void CopyGroups(IReadOnlyList<EventGroup> groups)
    {
        StringBuilder text = new();
        foreach (EventGroup group in groups)
        {
            if (text.Length > 0)
            {
                text.AppendLine().AppendLine();
            }

            text.Append(CopyText(group));
        }

        try
        {
            Clipboard.SetText(text.ToString());
            Navigator.ShowToast(new InAppToast(null, Strings.ToastCopied));
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            Diagnostics.ErrorLog.Write("Copy", ex);
        }
    }

    public static string CopyText(EventGroup group)
    {
        DiskEvent item = group.Latest;
        StringBuilder text = new();
        text.AppendLine(Strings.CopyHeader(item.Tier, item.Title));
        text.AppendLine(Format.ExactTime(item.TimeUtc));
        text.AppendLine(Strings.CopyDevice(item.DeviceText.Length > 0 ? item.DeviceText : Strings.KindName(item.Kind)));
        text.AppendLine(Strings.CopySource(item.Provider, item.EventId, item.Log));
        if (group.Count > 1)
        {
            text.AppendLine(Strings.CopyOccurrences(group.Count));
        }

        text.AppendLine();
        text.AppendLine(Strings.CopyLabelled(Strings.WhatThisMeans, item.Explanation));
        text.AppendLine(Strings.CopyLabelled(Strings.WhyItMattersLabel, item.WhyItMatters));
        text.AppendLine(Strings.CopyLabelled(Strings.WhatYouCanDo, item.Advice));
        text.AppendLine();
        text.Append(item.Message);
        return text.ToString();
    }

    internal void Export(IReadOnlyList<DiskEvent> events)
    {
        using SaveFileDialog dialog = new()
        {
            Filter = Strings.ExportFilter,
            FileName = Strings.ExportFileName(Clock.UtcNow.ToLocalTime()),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Navigator.Window) != DialogResult.OK)
        {
            return;
        }

        string path = dialog.FileName;
        List<DiskEvent> copy = [.. events];
        _ = Task.Run(() =>
        {
            try
            {
                CsvExport.Write(path, copy);
                BeginInvoke(() => Navigator.ShowToast(new InAppToast(Level.Ok, Strings.ToastExported(copy.Count), null, Strings.OpenFolder,
                    () => Shell.Open(Path.GetDirectoryName(path) ?? path))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diagnostics.ErrorLog.Write("Export", ex);
                BeginInvoke(() => Navigator.ShowToast(new InAppToast(Level.Critical, Strings.ToastExportFailed, Strings.ToastExportFailedText(ex.Message))));
            }
        });
    }

    internal IReadOnlyList<DiskEvent> SelectedEvents() => _list.SelectedRows.OfType<EventGroup>().SelectMany(g => g.Events).ToList();

    internal void MarkSelectedRead()
    {
        List<DiskEvent> events = [.. SelectedEvents()];
        ReadState before = Services.Events.CaptureReadState();
        Services.Events.MarkRead(events);
        void Undo() => Services.Events.RestoreReadState(before);
        Navigator.ShowToast(new InAppToast(Level.Ok, Strings.ToastMarkedRead(events.Count), null, Strings.Undo, Undo));
        Navigator.SetUndo(Undo);
    }

    internal void ClearSelection() => _list.ClearSelection();

    /// <summary>Render harness: opens the drawer on the first event row.</summary>
    internal void PreviewDrawer()
    {
        int index = _rows.ToList().FindIndex(r => r is EventGroup);
        if (index >= 0)
        {
            _list.SelectOnly(index);
            OpenDrawer((EventGroup)_rows[index]);
        }
    }

    /// <summary>Render harness: selects the first event rows.</summary>
    internal void PreviewSelection(int count)
    {
        List<int> indexes = _rows.Select((row, index) => (row, index)).Where(p => p.row is EventGroup).Select(p => p.index).Take(count).ToList();
        if (indexes.Count == 0)
        {
            return;
        }

        _list.SelectOnly(indexes[0]);
        foreach (int index in indexes.Skip(1))
        {
            _list.ToggleSelection(index);
        }
    }

    /// <summary>Render harness: filters that match nothing.</summary>
    internal void PreviewNoMatch()
    {
        _tiers.Add(Tier.Low);
        _kinds.Add(ErrorKind.Capacity);
        Apply();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.I) && FocusedGroup() is { } ignore)
        {
            Ignore(ignore);
            return true;
        }

        if (keyData == (Keys.Control | Keys.C) && !_search.Inner.Focused)
        {
            List<EventGroup> groups = _list.SelectedRows.OfType<EventGroup>().ToList();
            if (groups.Count == 0 && FocusedGroup() is { } focused)
            {
                groups.Add(focused);
            }

            if (groups.Count > 0)
            {
                CopyGroups(groups);
                return true;
            }
        }

        if (keyData == (Keys.Control | Keys.Enter) && FocusedGroup() is { } primary)
        {
            RunPrimary(primary);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private EventGroup? FocusedGroup() => _drawer.Visible ? _drawer.Group : _list.FocusedRow as EventGroup;

    // --- inner controls -------------------------------------------------------------------------------

    /// <summary>The list card: frame, optional search row, the list, its empty state, the pill and the selection bar.</summary>
    private sealed class ListFrame : KitControl
    {
        private readonly EventsPage _page;
        private RectangleF _pill;
        private int _pending;

        public ListFrame(EventsPage page)
        {
            _page = page;
            Backdrop = static t => t.Bg;
            SetStyle(ControlStyles.ContainerControl, true);
        }

        public int DividerX { get; set; } = -1;

        public int PendingCount
        {
            get => _pending;
            set
            {
                _pending = value;
                _page._list.TopPadding = value > 0 ? S(40) : 0;
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int inset = S(1);
            int top = inset;
            if (_page._search.Visible)
            {
                _page._search.SetBounds(S(12), S(6), Math.Min(S(280), Width - S(24)), S(32) - S(4));
                top = S(40);
            }

            bool selection = _page._selection.Visible;
            _page._list.BottomPadding = selection ? S(64) : 0;
            if (_page._empty.Visible)
            {
                // Nothing to list: the list keeps only its filter summary row; the empty state fills the rest.
                bool summary = _page._rows.Count > 0;
                int listHeight = summary ? S(32) : 0;
                _page._list.Visible = summary;
                _page._list.SetBounds(inset, top, Width - 2 * inset, Math.Max(1, listHeight));
                _page._empty.SetBounds(inset, top + listHeight, Width - 2 * inset, Height - top - listHeight - inset);
            }
            else
            {
                _page._list.Visible = true;
                _page._list.SetBounds(inset, top, Width - 2 * inset, Height - top - inset);
            }
            _page._empty.BringToFront();
            int barWidth = Math.Min(S(560), Width - S(32));
            _page._selection.SetBounds((Width - barWidth) / 2, Height - S(12) - S(48), barWidth, S(48));
            _page._selection.BringToFront();
        }

        protected override void PaintContent(Graphics g)
        {
            RectangleF r = ClientRectangle;
            Draw.FillRounded(g, r, Sf(8), T.Surface);
            Draw.StrokeRounded(g, r, Sf(8), T.Stroke, Sc);
            if (_page._selection.Visible)
            {
                Draw.Shadow(g, _page._selection.Bounds, S(8), Dpi);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_pending > 0)
            {
                Graphics g = e.Graphics;
                Draw.PrepareShapes(g);
                string text = Strings.NewEventsPill(_pending);
                int width = Badges.PillWidth(text, Dpi) + S(8);
                _pill = new RectangleF((Width - width) / 2f, S(10), width, S(24));
                Draw.FillRounded(g, _pill, _pill.Height / 2f, T.Accent);
                Draw.TextCentered(g, text, F(FontRole.CaptionStrong), _pill.X + _pill.Width / 2f, _pill.Y + _pill.Height / 2f, T.OnAccent);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pending > 0 && _pill.Contains(e.Location))
            {
                _page.ApplyPending();
            }
        }
    }

    /// <summary>The selection bar: count, Mark as read, Copy, Export, and a close button.</summary>
    private sealed class SelectionBar : KitControl
    {
        private readonly EventsPage _page;
        private readonly KitButton _read = new() { Text = Strings.MarkAsRead, Compact = true, Backdrop = static t => t.Raised };
        private readonly KitButton _copy = new() { Text = Strings.Copy, Compact = true, Backdrop = static t => t.Raised };
        private readonly KitButton _export = new() { Text = Strings.EventsExport, Compact = true, Backdrop = static t => t.Raised };
        private readonly KitButton _close = new() { Icon = IconName.Close, Compact = true, Variant = ButtonVariant.Subtle, Backdrop = static t => t.Raised, ToolTipText = Strings.ButtonClose, AccessibleName = Strings.ButtonClose };
        private int _count;

        public SelectionBar(EventsPage page)
        {
            _page = page;
            Backdrop = static t => t.Surface;
            Controls.AddRange([_read, _copy, _export, _close]);
            _read.Click += (_, _) => _page.MarkSelectedRead();
            _copy.Click += (_, _) => _page.CopyGroups(_page._list.SelectedRows.OfType<EventGroup>().ToList());
            _export.Click += (_, _) => _page.Export(_page.SelectedEvents());
            _close.Click += (_, _) => _page.ClearSelection();
        }

        public void Refresh(List<EventGroup> selected)
        {
            _count = selected.Count;
            bool show = _count >= 2;
            if (show != Visible)
            {
                Visible = show;
                Parent?.PerformLayout();
                Parent?.Invalidate();
            }

            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = S(104);
            foreach (KitButton button in new[] { _read, _copy, _export })
            {
                button.SizeToContent();
                button.Location = new Point(x, (Height - button.Height) / 2);
                x += button.Width + S(8);
            }

            _close.SetBounds(Width - S(10) - S(28), (Height - S(28)) / 2, S(28), S(28));
        }

        protected override void PaintContent(Graphics g)
        {
            RectangleF r = ClientRectangle;
            Draw.FillRounded(g, r, Sf(8), T.Raised);
            Draw.StrokeRounded(g, r, Sf(8), T.StrokeStrong, Sc);
            Draw.Text(g, Strings.SelectedCount(_count), F(FontRole.BodyStrong), Sf(16), Height / 2f, T.Text);
        }
    }
}

/// <summary>CSV export (subplan 05, section 7): every field quoted, UTF-8 with a byte-order mark.</summary>
internal static class CsvExport
{
    public static void Write(string path, IReadOnlyList<DiskEvent> events)
    {
        StringBuilder csv = new();
        csv.AppendLine(string.Join(",", Strings.CsvColumns.Select(Quote)));
        foreach (DiskEvent item in events)
        {
            string[] fields =
            [
                item.TimeLocal.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                Strings.TierName(item.Tier),
                Strings.KindName(item.Kind),
                item.Title,
                item.DeviceText,
                item.Volume ?? "",
                item.DiskNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                item.Log,
                item.Provider,
                item.EventId.ToString(CultureInfo.InvariantCulture),
                item.RecordId.ToString(CultureInfo.InvariantCulture),
                item.RuleId,
                item.Message
            ];
            csv.AppendLine(string.Join(",", fields.Select(Quote)));
        }

        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    internal static string Quote(string field) => "\"" + field.Replace("\"", "\"\"").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ') + "\"";
}
