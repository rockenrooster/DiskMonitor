using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>
/// Drives (subplan 06): "How healthy is each drive, by its own account?" One card per physical drive:
/// its self-reported health, the few numbers that matter, its volumes, and nothing that needs a manual.
/// </summary>
internal sealed class DrivesPage : PageBase
{
    private readonly KitButton _check = new() { Text = Strings.CheckNow, Icon = IconName.Refresh };
    private readonly Dictionary<int, DriveCard> _cards = [];
    private readonly EmptyStateControl _looking = new() { Headline = Strings.LookingForDrives, Loading = true, IconSize = 48, Visible = false };
    private int? _pendingDisk;

    public DrivesPage(AppServices services, INavigator navigator) : base(services, navigator)
    {
        _check.Click += (_, _) => _ = Services.Drives.RefreshAsync();
        Controls.Add(_looking);
    }

    public override string Key => "Drives";

    public override string Title => Strings.PageDrives;

    public override IReadOnlyList<Control> Toolbar => [_check];

    public override void Navigate(object argument)
    {
        if (argument is DiskTarget target)
        {
            _pendingDisk = target.DiskNumber;
            ScrollToPending();
        }
    }

    public override void RefreshData(RefreshReason reasons)
    {
        _check.Busy = Services.Drives.IsRefreshing;
        DriveSnapshot snapshot = Services.Drives.Current;
        bool none = snapshot.TakenUtc == DateTime.MinValue && snapshot.Disks.Count == 0;
        _looking.Visible = none;
        IReadOnlyList<DiskEvent> events = Services.Events.Snapshot();
        Dictionary<int, int> counts = [];
        foreach (DiskEvent item in events)
        {
            if (item.DiskNumber is { } disk)
            {
                counts[disk] = counts.GetValueOrDefault(disk) + 1;
            }
        }

        HashSet<int> present = [];
        foreach (PhysicalDisk disk in snapshot.Disks.OrderBy(d => d.Number))
        {
            present.Add(disk.Number);
            if (!_cards.TryGetValue(disk.Number, out DriveCard? card))
            {
                card = new DriveCard(this);
                _cards[disk.Number] = card;
                Controls.Add(card);
            }

            card.Update(disk, snapshot.TakenUtc, counts.GetValueOrDefault(disk.Number), Services.Settings);
        }

        foreach (int gone in _cards.Keys.Where(k => !present.Contains(k)).ToList())
        {
            Controls.Remove(_cards[gone]);
            _cards[gone].Dispose();
            _cards.Remove(gone);
        }

        RequestLayout();
        ScrollToPending();
    }

    public override int Arrange(int width, int viewportHeight)
    {
        if (_looking.Visible)
        {
            _looking.SetBounds(0, 0, width, viewportHeight);
            return viewportHeight;
        }

        int y = 0;
        foreach (DriveCard card in _cards.OrderBy(p => p.Key).Select(p => p.Value))
        {
            int height = card.Measure(width);
            card.SetBounds(0, y, width, height);
            y += height + S(12);
        }

        return Math.Max(viewportHeight, y - S(12));
    }

    internal void OpenEvents(int disk) => Navigator.ShowEvents(new EventFilter(DiskNumber: disk));

    internal void OpenCheckDisk(string volume) => Navigator.ShowCheckDisk(volume, null);

    private void ScrollToPending()
    {
        if (_pendingDisk is { } disk && _cards.TryGetValue(disk, out DriveCard? card) && Parent is PageViewport viewport)
        {
            _pendingDisk = null;
            PerformLayout();
            viewport.EnsureVisible(card.Bounds);
            card.Focus();
        }
    }

    /// <summary>One tile of readings: label, value, out-of-range tier, tooltip.</summary>
    internal sealed record Tile(string Label, string Value, Tier? Flag, string Tip);

    /// <summary>The metric tiles of a disk (subplan 06, section 4): only readings it reports, in the table's order.</summary>
    internal static List<Tile> BuildTiles(PhysicalDisk d, int warning)
    {
        List<Tile> tiles = [];
        if (d.TemperatureC is { } temperature)
        {
            tiles.Add(new Tile(Strings.TileTemperature, Format.Temperature(temperature), temperature >= warning ? Tier.Medium : null, Strings.TipTemperature));
        }

        if (d.LifeRemainingPercent is { } life)
        {
            tiles.Add(new Tile(Strings.TileLife, Format.Percent(life), life <= 10 ? Tier.Medium : null, Strings.TipLife));
        }

        if (d.PowerOnHours is { } hours)
        {
            tiles.Add(new Tile(Strings.TilePowerOn, Format.DriveAge(hours), null, Strings.TipPowerOn));
        }

        if (d.PowerCycles is { } cycles)
        {
            tiles.Add(new Tile(Strings.TileCycles, Format.Number(cycles), null, Strings.TipCycles));
        }

        if (d.BytesWritten is { } written)
        {
            tiles.Add(new Tile(Strings.TileWritten, Format.Size(written), null, Strings.TipWritten));
        }

        if (d.BytesRead is { } read)
        {
            tiles.Add(new Tile(Strings.TileRead, Format.Size(read), null, Strings.TipRead));
        }

        void Counter(long? value, string label, string tip, bool flag)
        {
            if (value is { } v)
            {
                tiles.Add(new Tile(label, Format.Number(v), flag && v > 0 ? Tier.Medium : null, tip));
            }
        }

        Counter(d.Reallocated, Strings.TileReallocated, Strings.TipReallocated, true);
        Counter(d.Pending, Strings.TilePending, Strings.TipPending, true);
        Counter(d.Uncorrectable, Strings.TileUncorrectable, Strings.TipUncorrectable, true);
        Counter(d.CrcErrors, Strings.TileCrc, Strings.TipCrc, false);
        Counter(d.MediaErrors, Strings.TileMedia, Strings.TipMedia, true);
        Counter(d.UnsafeShutdowns, Strings.TileUnsafe, Strings.TipUnsafe, false);
        if (d.AvailableSparePercent is { } spare)
        {
            bool low = d.SpareThresholdPercent is { } threshold && spare < threshold;
            tiles.Add(new Tile(Strings.TileSpare, Format.Percent(spare), low ? Tier.Critical : null, Strings.TipSpare));
        }

        return tiles;
    }

    /// <summary>A drive card (subplan 06, section 2).</summary>
    private sealed class DriveCard : Card
    {
        private readonly DrivesPage _page;
        private readonly Dictionary<string, KitButton> _scanButtons = [];
        private readonly LinkControl _eventsLink = new();
        private PhysicalDisk? _disk;
        private DateTime _checkedUtc;
        private int _eventCount;
        private int _historyDays;
        private int _lowSpace;
        private List<Tile> _tiles = [];
        private readonly List<RectangleF> _tileRects = [];
        private int _hoverTile = -1;

        public DriveCard(DrivesPage page)
        {
            _page = page;
            Backdrop = static t => t.Bg;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
            _eventsLink.Click += (_, _) => { if (_disk is not null) { _page.OpenEvents(_disk.Number); } };
            Controls.Add(_eventsLink);
        }

        public void Update(PhysicalDisk disk, DateTime checkedUtc, int events, AppSettings settings)
        {
            _disk = disk;
            _checkedUtc = checkedUtc;
            _eventCount = events;
            _historyDays = settings.HistoryDays;
            _lowSpace = settings.LowSpacePercent;
            AccessibleName = disk.DisplayName;
            AccessibleDescription = Strings.HealthName(disk.Health);
            _tiles = BuildTiles(disk, settings.TemperatureWarningC);

            HashSet<string> letters = [];
            foreach (VolumeInfo volume in disk.Volumes.Where(v => v.SupportsChkdsk))
            {
                letters.Add(volume.Letter);
                if (!_scanButtons.ContainsKey(volume.Letter))
                {
                    string letter = volume.Letter;
                    KitButton button = new() { Text = Strings.ScanEllipsis, Compact = true, Variant = ButtonVariant.Subtle, Backdrop = static t => t.Surface, AccessibleName = Strings.ActionScanVolume(letter) };
                    button.Click += (_, _) => _page.OpenCheckDisk(letter);
                    _scanButtons[letter] = button;
                    Controls.Add(button);
                }
            }

            foreach (string gone in _scanButtons.Keys.Where(k => !letters.Contains(k)).ToList())
            {
                Controls.Remove(_scanButtons[gone]);
                _scanButtons[gone].Dispose();
                _scanButtons.Remove(gone);
            }

            _eventsLink.Text = Strings.EventsInLastDays(events, _historyDays);
            _eventsLink.Visible = events > 0;
            PerformLayout();
            Invalidate(true);
        }

        private bool NoHealth => _disk is { HealthSupported: false };

        private bool Narrow => Width < S(760);

        /// <summary>
        /// Tiles 128 × 52, 8 apart, wrapping. A tile whose label or value would be cut grows to fit it, so
        /// no reading needs its tooltip to be understood.
        /// </summary>
        private List<RectangleF> LayoutTiles(int width, float top)
        {
            float s = Sc;
            Font label = F(FontRole.CaptionStrong);
            Font value = F(FontRole.BodyStrong);
            List<RectangleF> rects = new(_tiles.Count);
            float x = 16 * s;
            float rowTop = top;
            foreach (Tile tile in _tiles)
            {
                float valueWidth = Draw.TextWidth(tile.Value, value) + (tile.Flag is null ? 0 : 16 * s);
                float w = Math.Max(128 * s, (float)Math.Ceiling(Math.Max(Draw.TextWidth(tile.Label, label), valueWidth) + 24 * s));
                if (x + w > width - 16 * s + 0.5f && x > 16 * s)
                {
                    x = 16 * s;
                    rowTop += 60 * s;
                }

                rects.Add(new RectangleF(x, rowTop, w, 52 * s));
                x += w + 8 * s;
            }

            return rects;
        }

        /// <summary>Card height for a width: heading, facts, notes or info bars, tiles, volumes, footer.</summary>
        public int Measure(int width)
        {
            if (_disk is null)
            {
                return S(100);
            }

            int y = S(16) + S(28) + S(20) + S(8);
            if (NoHealth)
            {
                y += S(40) + S(12);
            }
            else
            {
                y += _disk.HealthNotes.Count * S(24);
                if (_disk.HealthNotes.Count > 0)
                {
                    y += S(8);
                }

                if (_disk.Asleep)
                {
                    y += S(40) + S(12);
                }

                if (_tiles.Count > 0)
                {
                    List<RectangleF> tiles = LayoutTiles(width, 0);
                    y += (int)Math.Ceiling(tiles[^1].Bottom) + S(16);
                }
            }

            if (_disk.Volumes.Count > 0)
            {
                y += S(32) + _disk.Volumes.Count * S(36) + S(12);
            }

            return y + S(20) + S(16);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_disk is null)
            {
                return;
            }

            int volumesTop = Height - S(16) - S(20) - S(12) - _disk.Volumes.Count * S(36);
            int i = 0;
            foreach (VolumeInfo volume in _disk.Volumes)
            {
                if (_scanButtons.TryGetValue(volume.Letter, out KitButton? button))
                {
                    button.SizeToContent();
                    button.Location = new Point(Width - S(16) - button.Width, volumesTop + i * S(36) + (S(36) - button.Height) / 2);
                }

                i++;
            }

            float s = Sc;
            string checkedText = Strings.HealthChecked(_checkedUtc);
            Font caption = F(FontRole.Caption);
            int x = (int)(16 * s + Draw.TextWidth(checkedText + Strings.Separator, caption));
            _eventsLink.SetBounds(x, Height - S(16) - S(20), Math.Max(1, Draw.TextWidth(_eventsLink.Text, caption) + S(4)), S(20));
        }

        protected override void PaintCard(Graphics g)
        {
            if (_disk is not { } disk)
            {
                return;
            }

            ThemeTokens t = T;
            float s = Sc;
            if (disk.Health is HealthStatus.Caution or HealthStatus.Bad)
            {
                Draw.FillRounded(g, new RectangleF(0, 12 * s, 3 * s, Height - 24 * s), 1.5f * s, t.HealthColors(disk.Health).Fill);
            }

            float y = 16 * s;
            Draw.Icon(g, IconName.Drive, 16 * s, y + 4 * s, 20, Dpi, t.Text2);
            int badge = Badges.HealthWidth(disk.Health, Dpi);
            float badgeX = Width - 16 * s - badge;
            Font subtitle = F(FontRole.Subtitle);
            Draw.Text(g, disk.DisplayName, subtitle, 48 * s, y + 14 * s, t.Text, (int)(badgeX - 56 * s));
            Badges.Health(g, badgeX, y + 4 * s, disk.Health, Dpi);
            y += 28 * s;

            List<string> facts = [Strings.DiskName(disk.Number), disk.BusLabel, disk.MediaLabel, disk.SizeBytes > 0 ? Format.Size(disk.SizeBytes) : "",
                disk.Firmware.Length > 0 ? Strings.FactFirmware(disk.Firmware) : "", disk.Serial.Length > 0 ? Strings.FactSerial(disk.Serial) : ""];
            Draw.Text(g, string.Join(Strings.Separator, facts.Where(f => f.Length > 0)), F(FontRole.Body), 16 * s, y + 10 * s, t.Text2, (int)(Width - 32 * s));
            y += 20 * s + 8 * s;

            if (NoHealth)
            {
                Surfaces.InfoBar(g, new RectangleF(16 * s, y, Width - 32 * s, 40 * s), Level.Low, Strings.NoHealthData, null, Dpi);
                y += 40 * s + 12 * s;
            }
            else
            {
                (Color text, _, _) = t.HealthColors(disk.Health);
                foreach (string note in disk.HealthNotes)
                {
                    Draw.Health(g, disk.Health, 16 * s, y + 6 * s, 12, Dpi, t.Surface);
                    Draw.Text(g, note, F(FontRole.Body), 36 * s, y + 12 * s, text, (int)(Width - 52 * s));
                    y += 24 * s;
                }

                if (disk.HealthNotes.Count > 0)
                {
                    y += 8 * s;
                }

                if (disk.Asleep)
                {
                    Surfaces.InfoBar(g, new RectangleF(16 * s, y, Width - 32 * s, 40 * s), Level.Low, Strings.Asleep(_checkedUtc), null, Dpi);
                    y += 40 * s + 12 * s;
                }

                _tileRects.Clear();
                if (_tiles.Count > 0)
                {
                    _tileRects.AddRange(LayoutTiles(Width, y));
                    for (int i = 0; i < _tiles.Count; i++)
                    {
                        DataGraphics.MetricTile(g, _tileRects[i], _tiles[i].Label, _tiles[i].Value, _tiles[i].Flag, Dpi, i == _hoverTile);
                    }

                    y = _tileRects[^1].Bottom + 16 * s;
                }
            }

            if (disk.Volumes.Count > 0)
            {
                bool narrow = Narrow;
                RectangleF header = new(s, y, Width - 2 * s, 32 * s);
                RowPaint.TableHeader(g, header, [(Strings.ColumnVolume, 16 * s, false), (Strings.ColumnFileSystem, (16 + 220) * s, false),
                    (narrow ? Strings.ColumnFree : Strings.ColumnSpace, (16 + 320) * s, false), (narrow ? "" : Strings.ColumnFree, (16 + 540) * s, false)], Dpi);
                y += 32 * s;
                foreach (VolumeInfo volume in disk.Volumes)
                {
                    float cy = y + 18 * s;
                    Font strong = F(FontRole.BodyStrong);
                    Draw.Text(g, volume.Letter, strong, 16 * s, cy, t.Text);
                    float labelX = 16 * s + Draw.TextWidth(volume.Letter, strong) + 8 * s;
                    Draw.Text(g, volume.Label, F(FontRole.Body), labelX, cy, t.Text2, (int)(200 * s - labelX + 16 * s));
                    if (volume.Dirty == true)
                    {
                        Badges.Pill(g, labelX + Draw.TextWidth(volume.Label, F(FontRole.Body)) + 8 * s, cy - 10 * s, Strings.NeedsCheck, Level.Medium, Dpi);
                    }

                    Draw.Text(g, volume.FileSystem, F(FontRole.Body), (16 + 220) * s, cy, t.Text2);
                    float freeX = narrow ? (16 + 320) * s : (16 + 540) * s;
                    if (!narrow)
                    {
                        DataGraphics.UsageBar(g, new RectangleF((16 + 320) * s, cy - 2 * s, 200 * s, 4 * s), volume.UsedFraction, volume.FreePercent, _lowSpace);
                    }

                    Draw.Text(g, Strings.FreeOf(volume.FreeBytes, volume.TotalBytes), F(FontRole.Body), freeX, cy, t.Text2);
                    RowPaint.Divider(g, 16 * s, Width - 16 * s, y + 36 * s, Dpi);
                    y += 36 * s;
                }
            }

            float footer = Height - 16 * s - 10 * s;
            string checkedText = _eventCount > 0 ? Strings.HealthChecked(_checkedUtc) + Strings.Separator : Strings.HealthChecked(_checkedUtc);
            Draw.Text(g, checkedText, F(FontRole.Caption), 16 * s, footer, t.Text2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = _tileRects.FindIndex(r => r.Contains(e.Location));
            if (hover != _hoverTile)
            {
                _hoverTile = hover;
                ToolTipText = hover >= 0 ? _tiles[hover].Tip : null;
                KitTooltip.Hide(this);
                ResetMouseEventArgs();
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverTile = -1;
            Invalidate();
        }
    }
}

/// <summary>An inline link: Caption in Accent, underlined on hover and focus; Enter or a click follows it.</summary>
internal sealed class LinkControl : InteractiveKitControl
{
    public LinkControl()
    {
        Backdrop = static t => t.Surface;
        AccessibleRole = AccessibleRole.Link;
        Cursor = Cursors.Hand;
    }

    public FontRole Role { get; set; } = FontRole.Caption;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value; AccessibleName = value; Invalidate(); }
    }

    public override bool FocusRingVisible => false;

    protected override void PaintContent(Graphics g)
    {
        Font font = F(Role);
        Color color = ShowHover ? T.AccentHover : T.Accent;
        Draw.Text(g, Text, font, 0, Height / 2f, color, Width);
        if (ShowHover || ShowFocus)
        {
            float y = (float)Math.Floor(Height / 2f + Sf(Role == FontRole.Caption ? 7 : 9)) + Sf(0.5f);
            Draw.Line(g, color, Sc, 0, y, Math.Min(Width, Draw.TextWidth(Text, font)), y);
        }
    }
}
