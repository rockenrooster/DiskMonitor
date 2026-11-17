using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Core.Drives;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>
/// Check Disk (subplan 07): "Is the file system intact, and can it be repaired?" Run CHKDSK without a
/// command prompt, watch it work, and let Disk Monitor check on its own. Nothing here starts a scan
/// except the user's click, or a proactive option the user turned on.
/// </summary>
internal sealed class CheckDiskPage : PageBase
{
    // Volumes card geometry (logical px, relative to the card).
    private const int TableTop = 40;
    private const int HeaderHeight = 32;
    private const int RowHeight = 36;

    private readonly KitButton _scanAll = new() { Text = Strings.ScanAll, Icon = IconName.Play, DisabledReason = Strings.ScanAlreadyRunning };
    private readonly InfoBarControl _offer = new() { Visible = false, Backdrop = static t => t.Bg };
    private readonly TargetCard _volumes = new() { Title = Strings.CardVolumes };
    private readonly SplitButton _split = new() { DisabledReason = Strings.ScanAlreadyRunning, Backdrop = static t => t.Surface };
    private readonly ScanCard _scan;
    private readonly ProactiveCard _proactive;
    private List<VolumeInfo> _list = [];
    private Dictionary<string, ScanRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private string? _selected;
    private string? _offerVolume;
    private string _hint = Strings.HintPrompt;
    private bool _registered;
    private bool _focusOffer;
    private bool _ringAnimating;

    public CheckDiskPage(AppServices services, INavigator navigator) : base(services, navigator)
    {
        _scan = new ScanCard(this);
        _proactive = new ProactiveCard(this);
        _scanAll.Click += (_, _) => ScanAll();
        _offer.ActionClicked += (_, _) => OnOfferAction();
        _offer.Dismissed += (_, _) => HideOffer();
        _volumes.Painter = PaintVolumes;
        _volumes.SetKeyboardTargets(true);
        _volumes.KeyOverride = OnVolumesKey;
        _volumes.AccessibleName = Strings.CardVolumes;
        _split.Click += (_, _) => { if (_selected is { } volume) { StartScan(volume, ChkdskMode.Scan); } };
        _split.MenuRequested += (_, _) => ShowScanMenu();
        _volumes.Controls.Add(_split);
        Controls.AddRange([_offer, _volumes, _scan, _proactive]);
    }

    public override string Key => "CheckDisk";

    public override string Title => Strings.PageCheckDisk;

    public override IReadOnlyList<Control> Toolbar => [_scanAll];

    internal AppServices AppServices => Services;

    internal INavigator Nav => Navigator;

    internal string? SelectedVolume => _selected;

    internal bool TaskRegistered => _registered;

    /// <summary>The most recent scan record of any volume, for the scan card's empty state.</summary>
    internal ScanRecord? LastRecord => _records.Values.OrderByDescending(r => r.TimeUtc).FirstOrDefault();

    /// <summary>Selects a volume; a reason shows the offer bar. Nothing starts until the user acts.</summary>
    public void SelectVolume(string letter, ScanOfferReason? reason)
    {
        if (ChkdskRunner.NormalizeVolume(letter) is not { } volume)
        {
            return;
        }

        _selected = volume;
        if (reason is not null)
        {
            _offerVolume = volume;
            string text = reason.ReportedUtc is { } reported ? Strings.OfferDamage(volume, reported) : Strings.OfferAsk(volume);
            _offer.Configure(Level.High, text, Strings.OfferAction(volume), dismissible: true);
            _offer.Visible = true;
            _focusOffer = true;
        }

        RefreshData(RefreshReason.All);
        FocusOfferWhenReady();
    }

    public override void Navigate(object argument)
    {
        if (argument is ScanOffer offer && offer.Volume is { } volume)
        {
            SelectVolume(volume, offer.Reason);
        }
    }

    public override void Activate()
    {
        UpdateHint();
        FocusOfferWhenReady();
    }

    public override void RefreshData(RefreshReason reasons)
    {
        _list = Services.Drives.Current.Volumes.OrderBy(v => v.Letter, StringComparer.OrdinalIgnoreCase).ToList();
        _records = Services.State.Read(s => new Dictionary<string, ScanRecord>(s.Scans, StringComparer.OrdinalIgnoreCase));
        if (_selected is null || !_list.Any(v => v.SupportsChkdsk && v.Letter.Equals(_selected, StringComparison.OrdinalIgnoreCase)))
        {
            _selected = (_list.FirstOrDefault(v => v.IsSystem && v.SupportsChkdsk) ?? _list.FirstOrDefault(v => v.SupportsChkdsk))?.Letter;
        }

        bool running = Services.Chkdsk.IsRunning;
        SetRingAnimating(running);
        _scanAll.Enabled = !running;
        _split.Enabled = !running && _selected is not null;
        string label = Strings.OfferAction(_selected ?? "");
        if (_split.Text != label)
        {
            _split.Text = label;
        }

        _scan.Refresh();
        _proactive.Refresh();
        RequestLayout();
        _volumes.Invalidate();
    }

    public override int Arrange(int width, int viewportHeight)
    {
        int y = 0;
        if (_offer.Visible)
        {
            _offer.SetBounds(0, 0, width, S(40));
            y = S(40) + S(12);
        }

        int volumesHeight = S(TableTop + HeaderHeight) + Math.Max(1, _list.Count) * S(RowHeight) + S(12 + 32 + 16);
        if (width >= S(900))
        {
            int right = S(340);
            int left = width - right - S(12);
            int proactiveHeight = _proactive.Measure(right);
            _volumes.SetBounds(0, y, left, volumesHeight);
            int scanTop = y + volumesHeight + S(12);
            int bottom = Math.Max(viewportHeight, Math.Max(scanTop + S(220), y + proactiveHeight));
            _scan.SetBounds(0, scanTop, left, bottom - scanTop);
            _proactive.SetBounds(left + S(12), y, right, bottom - y);
            LayoutSplit();
            return bottom;
        }

        _volumes.SetBounds(0, y, width, volumesHeight);
        y += volumesHeight + S(12);
        _scan.SetBounds(0, y, width, S(300));
        y += S(300) + S(12);
        int height = _proactive.Measure(width);
        _proactive.SetBounds(0, y, width, height);
        LayoutSplit();
        return y + height;
    }

    internal void Relayout() => RequestLayout();

    /// <summary>Runs <paramref name="action"/> on the UI thread; drops it when the window has closed.</summary>
    internal void PostToUi(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(action);
            }
        }
        catch (InvalidOperationException)
        {
            // The window closed meanwhile.
        }
    }

    /// <summary>Works out the approval hint on a pool thread: a process query never runs in layout, painting or a click.</summary>
    private void UpdateHint()
    {
        if (Elevation.IsElevated)
        {
            SetRegistered(_registered);
            return;
        }

        bool demo = Services.DemoMode;
        _ = Task.Run(() =>
        {
            bool registered = !demo && ScanTask.IsRegistered();
            PostToUi(() => SetRegistered(registered));
        });
    }

    internal void SetRegistered(bool registered)
    {
        _registered = registered;
        _hint = Elevation.IsElevated ? Strings.HintAdmin : registered ? Strings.HintTask : Strings.HintPrompt;
        _proactive.Refresh();
        _volumes.Invalidate();
    }

    /// <summary>
    /// The scanning row's progress ring turns with the shared animation clock, but the table repaints only
    /// when its data changes, so the ring stood still between progress updates. While a run is going its
    /// ring area repaints on every animation tick.
    /// </summary>
    private void SetRingAnimating(bool on)
    {
        if (on == _ringAnimating)
        {
            return;
        }

        _ringAnimating = on;
        if (on)
        {
            KitAnimator.Add(OnRingTick);
        }
        else
        {
            KitAnimator.Remove(OnRingTick);
        }
    }

    private void OnRingTick(long now)
    {
        if (RunningRowRing() is { } ring)
        {
            _volumes.Invalidate(ring);
        }
    }

    internal bool RingAnimating => _ringAnimating;

    /// <summary>The running volume's ring in the volumes card, or null when no listed volume is running.</summary>
    internal Rectangle? RunningRowRing()
    {
        string? running = Services.Chkdsk.RunningVolume;
        int index = running is null ? -1 : _list.FindIndex(v => v.Letter.Equals(running, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        float s = Sc;
        float top = (TableTop + HeaderHeight + index * RowHeight) * s;
        return Rectangle.FromLTRB((int)Math.Floor(458 * s), (int)Math.Floor(top), (int)Math.Ceiling(478 * s), (int)Math.Ceiling(top + RowHeight * s));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SetRingAnimating(false);
        }

        base.Dispose(disposing);
    }

    private void FocusOfferWhenReady()
    {
        if (_focusOffer && _offer.Visible && _offer.CanFocus)
        {
            _focusOffer = false;
            _offer.Focus();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_focusOffer)
        {
            PostToUi(FocusOfferWhenReady);
        }
    }

    private void OnOfferAction()
    {
        string? volume = _offerVolume;
        HideOffer();
        if (volume is not null)
        {
            StartScan(volume, ChkdskMode.Scan);
        }
    }

    private void HideOffer()
    {
        _offer.Visible = false;
        _offerVolume = null;
        _focusOffer = false;
        RequestLayout();
    }

    private void ScanAll()
    {
        if (!Services.Chkdsk.IsRunning && !Services.DemoMode)
        {
            _ = Services.Proactive.RunRoutineAsync(automatic: false);
        }
    }

    private void LayoutSplit()
    {
        int top = S(TableTop + HeaderHeight) + Math.Max(1, _list.Count) * S(RowHeight) + S(12);
        _split.SetBounds(S(16), top, _split.PreferredWidth, S(32));
    }

    private void Select(string letter)
    {
        _selected = letter;
        RefreshData(RefreshReason.None);
    }

    /// <summary>Up and Down move the single selection; Left and Right do nothing in this table.</summary>
    private bool OnVolumesKey(Keys key)
    {
        if (key is Keys.Left or Keys.Right)
        {
            return true;
        }

        if (key is not (Keys.Up or Keys.Down))
        {
            return false;
        }

        List<VolumeInfo> selectable = _list.Where(v => v.SupportsChkdsk).ToList();
        if (selectable.Count > 0)
        {
            int index = selectable.FindIndex(v => v.Letter.Equals(_selected, StringComparison.OrdinalIgnoreCase));
            index = Math.Clamp(index + (key == Keys.Down ? 1 : -1), 0, selectable.Count - 1);
            Select(selectable[index].Letter);
        }

        return true;
    }

    private void PaintVolumes(Graphics g, TargetCard card)
    {
        ThemeTokens t = T;
        float s = Sc;
        Font body = F(FontRole.Body);
        Font strong = F(FontRole.BodyStrong);
        RowPaint.TableHeader(g, new RectangleF(s, TableTop * s, card.Width - 2 * s, HeaderHeight * s),
            [(Strings.ColumnVolume, 16 * s, false), (Strings.ColumnFileSystem, 260 * s, false), (Strings.ColumnSize, 440 * s, true), (Strings.ColumnLastScan, 460 * s, false)], Dpi);
        float y = (TableTop + HeaderHeight) * s;
        int focus = -1;
        foreach (VolumeInfo volume in _list)
        {
            RectangleF row = new(s, y, card.Width - 2 * s, RowHeight * s);
            bool selectable = volume.SupportsChkdsk;
            bool selected = selectable && volume.Letter.Equals(_selected, StringComparison.OrdinalIgnoreCase);
            bool hover = selectable && !selected && card.PointerLocation is { } pointer && row.Contains(pointer);
            RowPaint.Background(g, row, hover, selected, 24 * s, Dpi);
            bool onSelected = selected && t.IsHighContrast;
            Color text = !selectable ? t.TextDisabled : onSelected ? t.OnSelected : t.Text;
            Color text2 = !selectable ? t.TextDisabled : onSelected ? t.OnSelected : t.Text2;
            float cy = y + RowHeight * s / 2f;
            Draw.Text(g, volume.Letter, strong, 16 * s, cy, text);
            string label = !volume.IsSystem ? volume.Label
                : string.IsNullOrWhiteSpace(volume.Label) ? Strings.WindowsVolumeUnlabelled
                : volume.Label.Equals(Strings.WindowsVolumeUnlabelled, StringComparison.OrdinalIgnoreCase) ? volume.Label
                : volume.Label + Strings.WindowsSuffix;
            float labelX = 16 * s + Draw.TextWidth(volume.Letter, strong) + 6 * s;
            Draw.Text(g, label, body, labelX, cy, text2, (int)(244 * s - labelX));
            Draw.Text(g, volume.FileSystem, body, 260 * s, cy, text2, (int)(80 * s));
            Draw.TextRight(g, Format.Size(volume.TotalBytes), body, 440 * s, cy, text);
            PaintLastScan(g, volume, 460 * s, cy, selectable, onSelected);
            if (selectable)
            {
                if (selected)
                {
                    focus = card.Targets.Count;
                }

                string letter = volume.Letter;
                card.AddTarget(new CardTarget(row, () => Select(letter), Strings.VolumeName(volume.Letter, volume.Label)));
            }

            RowPaint.Divider(g, 16 * s, card.Width - 16 * s, y + RowHeight * s, Dpi);
            y += RowHeight * s;
        }

        card.SetFocusTarget(focus);

        // The approval hint, to the right of the split button.
        float hintY = (TableTop + HeaderHeight) * s + Math.Max(1, _list.Count) * RowHeight * s + 12 * s + 16 * s;
        float hintX = 16 * s + _split.Width + 16 * s;
        Draw.Icon(g, IconName.Shield, hintX, hintY - 8 * s, 16, Dpi, t.Text2);
        Draw.Text(g, _hint, F(FontRole.Caption), hintX + 24 * s, hintY, t.Text2, (int)(card.Width - hintX - 40 * s));
    }

    private void PaintLastScan(Graphics g, VolumeInfo volume, float x, float cy, bool selectable, bool onSelected)
    {
        ThemeTokens t = T;
        float s = Sc;
        Font body = F(FontRole.Body);
        if (!selectable)
        {
            Draw.Text(g, Strings.RefsRepairsItself, body, x, cy, t.TextDisabled);
            return;
        }

        ChkdskService chkdsk = Services.Chkdsk;
        if (chkdsk.IsRunning && volume.Letter.Equals(chkdsk.RunningVolume, StringComparison.OrdinalIgnoreCase))
        {
            DataGraphics.Ring(g, x + 8 * s, cy, 8 * s, Dpi, KitAnimator.Now);
            Draw.Text(g, Strings.ScanningPercent(chkdsk.IsWaitingForApproval ? null : chkdsk.ProgressPercent), body, x + 24 * s, cy, onSelected ? t.OnSelected : t.Text);
            return;
        }

        if (volume.Dirty == true)
        {
            Badges.Pill(g, x, cy - 10 * s, Strings.NeedsCheck, Level.Medium, Dpi);
            return;
        }

        if (!_records.TryGetValue(volume.Letter, out ScanRecord? record))
        {
            Draw.State(g, StateIcon.Pending, x, cy - 8 * s, 16, Dpi, onSelected ? t.OnSelected : t.Text3);
            Draw.Text(g, Strings.NeverScanned, body, x + 24 * s, cy, onSelected ? t.OnSelected : t.Text3);
            return;
        }

        (string word, Level level) = record.Outcome switch
        {
            nameof(ChkdskOutcome.Failed) => (Strings.PillCouldNotRun, Level.Critical),
            _ when record.Clean => (Strings.PillClean, Level.Ok),
            _ => (Strings.PillProblems, Level.High)
        };
        int width = Badges.Pill(g, x, cy - 10 * s, word, level, Dpi);
        Draw.Text(g, Format.RecentDate(record.TimeUtc, inSentence: false), body, x + width + 8 * s, cy, onSelected ? t.OnSelected : t.Text2);
    }

    private void ShowScanMenu()
    {
        if (_selected is not { } volume || Services.Chkdsk.IsRunning)
        {
            return;
        }

        bool ntfs = Services.Drives.Current.FindVolume(volume)?.SupportsOnlineScan == true;
        List<MenuEntry> entries =
        [
            new(Strings.MenuScanReadOnly, IconName.Play, Run: () => StartScan(volume, ChkdskMode.Scan)),
            MenuEntry.Separator,
            new(Strings.MenuSpotFix, IconName.Tools, Run: () => ConfirmRepair(volume, ChkdskMode.SpotFix)) { Enabled = ntfs, Tooltip = ntfs ? null : Strings.SpotFixNeedsNtfs },
            new(Strings.MenuRepair, IconName.Tools, Run: () => ConfirmRepair(volume, ChkdskMode.Fix)),
            new(Strings.MenuRepairRecover, IconName.Tools, Run: () => ConfirmRepair(volume, ChkdskMode.FixAndRecover))
        ];
        Rectangle bounds = _split.ScreenBounds;
        KitMenu.ShowAt(entries, new Point(bounds.Left, bounds.Bottom + S(4)), Dpi, Navigator.Window);
    }

    /// <summary>The repair confirmation (subplan 07, section 5): Cancel holds the focus because a repair changes the disk.</summary>
    private void ConfirmRepair(string volume, ChkdskMode mode)
    {
        if (Services.Chkdsk.IsRunning)
        {
            return;
        }

        bool system = Services.Drives.Current.FindVolume(volume)?.IsSystem == true;
        string first = mode switch
        {
            ChkdskMode.SpotFix => Strings.RepairSpotFixText,
            ChkdskMode.Fix => Strings.RepairFixText,
            _ => Strings.RepairRecoverText
        };
        using KitDialog dialog = new KitDialog(Strings.RepairTitle(volume), 480, cancelResult: 0)
            .Paragraph(first)
            .Paragraph(system ? Strings.RepairWindowsText(volume) : Strings.RepairOtherText)
            .Fact(IconName.Shield, Strings.RepairApproval)
            .Button(new DialogButton(Strings.Cancel, ButtonVariant.Secondary, 0, InitialFocus: true))
            .Button(new DialogButton(system ? Strings.RepairSchedule : Strings.RepairConfirm(volume), ButtonVariant.Primary, 1));
        if (dialog.ShowOver(Navigator.Window) == 1)
        {
            StartScan(volume, mode);
        }
    }

    internal void StartScan(string volume, ChkdskMode mode)
    {
        if (Services.Chkdsk.IsRunning || Services.DemoMode)
        {
            return;
        }

        _ = Services.Chkdsk.RunAsync(volume, mode, automatic: false);
        RefreshData(RefreshReason.Chkdsk);
    }

    /// <summary>
    /// The scan card (subplan 07, section 4): the empty state until something runs this session, then the
    /// run's heading, progress or result, and CHKDSK's console output.
    /// </summary>
    private sealed class ScanCard : Card, IThemeAware
    {
        private const int HeadingY = 56;
        private const int SublineY = 80;
        private const int ProgressY = 120;
        private const int ResultTop = 100;
        private const int ConsoleTop = 152;

        private readonly CheckDiskPage _page;
        private readonly EmptyStateControl _empty = new() { Icon = IconName.Scans, IconSize = 32, Headline = Strings.NoScanRunning, Backdrop = static t => t.Surface };
        private readonly InfoBarControl _result = new() { Visible = false, Backdrop = static t => t.Surface };
        private readonly TextBox _console = new() { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, WordWrap = true, TabStop = false, Visible = false };
        private readonly KitButton _copy = new() { Icon = IconName.Copy, Variant = ButtonVariant.Subtle, Compact = true, ToolTipText = Strings.CopyOutput, AccessibleName = Strings.CopyOutput, Backdrop = static t => t.Bg, Visible = false };
        private readonly KitButton _stop = new() { Text = Strings.ButtonStop, Backdrop = static t => t.Surface, Visible = false };
        private int _lines;
        private string[] _output = [];
        private bool _animating;

        public ScanCard(CheckDiskPage page)
        {
            _page = page;
            Title = Strings.CardScan;
            Backdrop = static t => t.Bg;
            _empty.ButtonClicked += (_, _) => { if (_page.SelectedVolume is { } volume) { _page.StartScan(volume, ChkdskMode.Scan); } };
            _result.ActionClicked += (_, _) =>
            {
                if (_page.AppServices.Chkdsk.LastResult is { Outcome: ChkdskOutcome.Problems } last)
                {
                    _page.ConfirmRepair(last.Volume, ChkdskMode.Fix);
                }
            };
            _copy.Click += (_, _) => CopyOutput();
            _stop.Click += (_, _) => ShellRoot.ConfirmStop(_page.Nav.Window, _page.AppServices.Chkdsk);
            Controls.AddRange([_empty, _result, _console, _copy, _stop]);
        }

        private bool ShowsRun
        {
            get
            {
                ChkdskService chkdsk = _page.AppServices.Chkdsk;
                return chkdsk.IsRunning || chkdsk.LastResult is not null;
            }
        }

        private Rectangle ConsoleInset => new(S(16), S(ConsoleTop), Math.Max(0, Width - S(32)), Math.Max(S(40), Height - S(ConsoleTop) - S(16)));

        /// <summary>Feeds the console with the new lines only (all of them again when the buffer was trimmed) and updates the state.</summary>
        public new void Refresh()
        {
            ChkdskService chkdsk = _page.AppServices.Chkdsk;
            string[] output = chkdsk.GetOutput();
            if (output.Length < _lines)
            {
                _console.Clear();
                _lines = 0;
            }

            if (output.Length > _lines)
            {
                _console.AppendText((_lines > 0 ? Environment.NewLine : "") + string.Join(Environment.NewLine, output[_lines..]));
                _lines = output.Length;
            }

            _output = output;
            bool shows = ShowsRun;
            bool running = chkdsk.IsRunning;
            _empty.Visible = !shows;
            if (!shows)
            {
                ScanRecord? last = _page.LastRecord;
                _empty.Message = last is null ? Strings.NoVolumeScanned : Strings.LastScanSentence(last.TimeUtc);
                _empty.ButtonText = Strings.OfferAction(_page.SelectedVolume ?? "");
                _empty.Invalidate();
            }

            _console.Visible = shows && output.Length > 0;
            _copy.Visible = shows && output.Length > 0;
            _stop.Visible = running && !chkdsk.IsWaitingForApproval && chkdsk.RunningMode.IsReadOnly();
            ChkdskResult? result = running ? null : chkdsk.LastResult;
            _result.Visible = result is not null;
            if (result is not null)
            {
                (Level level, string text) = MainForm.CheckDiskResultText(result);
                _result.Configure(level, text, result.Outcome == ChkdskOutcome.Problems ? Strings.MenuRepair : null);
            }

            SetAnimating(running);
            PerformLayout();
            Invalidate();
        }

        public void ApplyTheme()
        {
            // The back colour is set after ReadOnly, which would otherwise reset it to the system colour.
            _console.ReadOnly = true;
            _console.BackColor = T.Bg;
            _console.ForeColor = T.Text;
            ThemeService.ApplyNativeTheme(_console);
        }

        private void SetAnimating(bool on)
        {
            if (on == _animating)
            {
                return;
            }

            _animating = on;
            if (on)
            {
                KitAnimator.Add(OnTick);
            }
            else
            {
                KitAnimator.Remove(OnTick);
            }
        }

        private void OnTick(long now) => Invalidate(new Rectangle(0, 0, Width, S(ConsoleTop)));

        private void CopyOutput()
        {
            if (_output.Length == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, _output));
                _page.Nav.ShowToast(new InAppToast(null, Strings.ToastCopied));
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                Diagnostics.ErrorLog.Write("Copy output", ex);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _console.Font = F(FontRole.Mono);
            ApplyTheme();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            _console.Font = F(FontRole.Mono);
            base.OnDpiChangedAfterParent(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SetAnimating(false);
            }

            base.Dispose(disposing);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            _empty.SetBounds(S(1), S(40), Math.Max(0, Width - S(2)), Math.Max(0, Height - S(41)));
            _result.SetBounds(S(16), S(ResultTop), Math.Max(0, Width - S(32)), S(40));
            Rectangle inset = ConsoleInset;
            _console.SetBounds(inset.X + S(8), inset.Y + S(8), Math.Max(0, inset.Width - S(16) - S(32)), Math.Max(0, inset.Height - S(16)));
            _copy.SetBounds(inset.Right - S(4) - S(28), inset.Y + S(4), S(28), S(28));
            _stop.SizeToContent();
            _stop.Location = new Point(Width - S(16) - _stop.Width, S(HeadingY) - _stop.Height / 2);
        }

        protected override void PaintCard(Graphics g)
        {
            if (!ShowsRun)
            {
                return;
            }

            ThemeTokens t = T;
            float s = Sc;
            ChkdskService chkdsk = _page.AppServices.Chkdsk;
            Font subtitle = F(FontRole.Subtitle);
            Font caption = F(FontRole.Caption);
            int textWidth = Width - S(32) - (_stop.Visible ? _stop.Width + S(12) : 0);
            if (chkdsk.IsRunning && chkdsk.IsWaitingForApproval)
            {
                DataGraphics.Ring(g, 26 * s, HeadingY * s, 10 * s, Dpi, KitAnimator.Now);
                Draw.Text(g, Strings.WaitingApprovalHeading, subtitle, 44 * s, HeadingY * s, t.Text, textWidth - S(28));
            }
            else if (chkdsk.IsRunning && chkdsk.RunningVolume is { } volume)
            {
                ChkdskMode mode = chkdsk.RunningMode;
                Draw.Text(g, Strings.RunHeading(volume, !mode.IsReadOnly()), subtitle, 16 * s, HeadingY * s, t.Text, textWidth);
                Draw.Text(g, Strings.RunSubline(mode, chkdsk.RunStartedUtc, chkdsk.RunningTrigger), caption, 16 * s, SublineY * s, t.Text2, textWidth);
                PaintProgress(g, chkdsk);
            }
            else if (chkdsk.LastResult is { } result)
            {
                Draw.Text(g, Strings.RunHeading(result.Volume, !result.Mode.IsReadOnly()), subtitle, 16 * s, HeadingY * s, t.Text, textWidth);
                Draw.Text(g, Strings.RunSubline(result.Mode, result.StartedUtc, result.Trigger), caption, 16 * s, SublineY * s, t.Text2, textWidth);
            }

            Rectangle inset = ConsoleInset;
            Draw.FillRounded(g, inset, 4 * s, t.Bg);
            if (t.IsHighContrast)
            {
                Draw.StrokeRounded(g, inset, 4 * s, t.Stroke, s);
            }

            if (_output.Length == 0)
            {
                Draw.TextCentered(g, Strings.ConsoleEmpty, F(FontRole.Body), inset.X + inset.Width / 2f, inset.Y + inset.Height / 2f, t.Text2);
            }
        }

        /// <summary>Progress bar (8 high) with the percentage beside it, then <c>Time left</c> at the right.</summary>
        private void PaintProgress(Graphics g, ChkdskService chkdsk)
        {
            ThemeTokens t = T;
            float s = Sc;
            Font caption = F(FontRole.Caption);
            Font body = F(FontRole.Body);
            float cy = ProgressY * s;
            string left = chkdsk.TimeLeft is { } span ? Format.Duration(span, estimate: true) : Strings.Estimating;
            float valueWidth = Draw.TextWidth(left, body);
            float labelWidth = Draw.TextWidth(Strings.TimeLeft, caption);
            float groupX = Width - 16 * s - valueWidth - 8 * s - labelWidth;
            Draw.Text(g, Strings.TimeLeft, caption, groupX, cy, t.Text3);
            Draw.Text(g, left, body, groupX + labelWidth + 8 * s, cy, t.Text);
            int? percent = chkdsk.ProgressPercent;
            float barRight = groupX - 24 * s - Draw.TextWidth(Format.Percent(100), body) - 12 * s;
            DataGraphics.ProgressBar(g, new RectangleF(16 * s, cy - 4 * s, Math.Max(0, barRight - 16 * s), 8 * s), percent / 100.0, KitAnimator.Now);
            if (percent is { } p)
            {
                Draw.Text(g, Format.Percent(p), body, barRight + 12 * s, cy, t.Text);
            }
        }
    }

    /// <summary>
    /// The proactive protection card (subplan 07, section 6). Every change applies and saves at once. A
    /// helper that does not fit beside its control wraps, and its row grows.
    /// </summary>
    private sealed class ProactiveCard : Card
    {
        private const int RowMinimum = 56;
        private const int TaskRow = 4;

        private readonly CheckDiskPage _page;
        private readonly KitSwitch _auto = new() { AccessibleName = Strings.ProactiveAutoLabel };
        private readonly DropDown _routine = new() { AccessibleName = Strings.ProactiveRoutineLabel, MarkChanged = false };
        private readonly KitSwitch _clean = new() { AccessibleName = Strings.ProactiveCleanLabel };
        private readonly KitSwitch _perf = new() { AccessibleName = Strings.ProactivePerfLabel };
        private readonly KitSwitch _task = new() { AccessibleName = Strings.ProactiveTaskLabel };
        private readonly InfoBarControl _asks = new() { Visible = false, Backdrop = static t => t.Surface };
        private readonly Control[] _controls;
        private bool _busy;
        private bool _animating;

        public ProactiveCard(CheckDiskPage page)
        {
            _page = page;
            Title = Strings.CardProactive;
            Backdrop = static t => t.Bg;
            _routine.SetItems([Strings.RoutineOff, Strings.RoutineWeekly, Strings.RoutineMonthly]);
            _controls = [_auto, _routine, _clean, _perf, _task];
            foreach (KitControl control in new KitControl[] { _auto, _routine, _clean, _perf, _task })
            {
                control.Backdrop = static t => t.Surface;
            }

            Controls.AddRange([_asks, .. _controls]);
            AppSettings settings = page.AppServices.Settings;
            _auto.CheckedChanged += (_, _) => Save(() => settings.AutoScanOnError = _auto.Checked);
            _routine.SelectionChanged += (_, _) => Save(() => settings.RoutineScan = AppSettings.RoutineScanChoices[Math.Clamp(_routine.SelectedIndex, 0, AppSettings.RoutineScanChoices.Length - 1)]);
            _clean.CheckedChanged += (_, _) => Save(() => settings.NotifyCleanScan = _clean.Checked);
            _perf.CheckedChanged += (_, _) => Save(() => settings.ScanWithMoreResources = _perf.Checked);
            _task.CheckedChanged += (_, _) => ToggleTask(_task.Checked);
            _asks.ActionClicked += (_, _) =>
            {
                if (!_task.Checked && _task.Enabled)
                {
                    _task.Checked = true;
                    ToggleTask(true);
                }
            };
        }

        private readonly record struct RowLayout(string Label, List<string> Help, int Top, int Height);

        public int Measure(int width)
        {
            List<RowLayout> rows = Rows(width);
            return rows[^1].Top + rows[^1].Height + S(8);
        }

        public new void Refresh()
        {
            AppSettings settings = _page.AppServices.Settings;
            _auto.Checked = settings.AutoScanOnError;
            _routine.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.RoutineScanChoices, settings.RoutineScan));
            _clean.Checked = settings.NotifyCleanScan;
            _perf.Checked = settings.ScanWithMoreResources;
            bool elevated = Elevation.IsElevated;
            bool installed = AppPaths.IsInstalledBuild;
            if (!_busy)
            {
                _task.Checked = _page.TaskRegistered;
            }

            _task.Enabled = installed && !elevated && !_busy;
            _task.DisabledReason = _busy ? null : elevated ? Strings.ProactiveTaskElevated : Strings.ProactiveTaskPortable;
            bool automatic = settings.AutoScanOnError || settings.RoutineScan != AppSettings.RoutineScanChoices[0];
            bool asks = automatic && !elevated && !_page.TaskRegistered;
            _asks.Configure(Level.Low, Strings.ProactiveAsks, installed && !_busy ? Strings.ProactiveTaskLabel : null);
            if (asks != _asks.Visible)
            {
                _asks.Visible = asks;
                _page.Relayout();
            }

            SetAnimating(_busy);
            PerformLayout();
            Invalidate(true);
        }

        private void Save(Action change)
        {
            change();
            _page.AppServices.SaveSettings();
            Refresh();
        }

        /// <summary>
        /// Registers or removes the scan task on a pool thread (one UAC prompt); the switch is busy meanwhile
        /// and then takes its value from the task itself.
        /// </summary>
        private void ToggleTask(bool on)
        {
            if (_busy)
            {
                return;
            }

            if (_page.AppServices.DemoMode)
            {
                _task.Checked = _page.TaskRegistered;
                return;
            }

            _busy = true;
            Refresh();
            _ = Task.Run(() =>
            {
                bool ok = on ? ScanTask.RequestRegister() : ScanTask.RequestUnregister();
                bool registered = ScanTask.IsRegistered(refresh: true);
                _page.PostToUi(() =>
                {
                    _busy = false;
                    _page.SetRegistered(registered);
                    if (!ok && registered != on)
                    {
                        _page.Nav.ShowToast(new InAppToast(Level.Medium, Strings.NothingChanged, Strings.ApprovalDeclined));
                    }
                });
            });
        }

        private void SetAnimating(bool on)
        {
            if (on == _animating)
            {
                return;
            }

            _animating = on;
            if (on)
            {
                KitAnimator.Add(OnTick);
            }
            else
            {
                KitAnimator.Remove(OnTick);
            }
        }

        private void OnTick(long now) => Invalidate();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SetAnimating(false);
            }

            base.Dispose(disposing);
        }

        private int DropWidth() => _routine.Items.Max(item => Draw.TextWidth(item, F(FontRole.Body))) + S(12 + 8 + 26);

        private int ControlWidth(int row) => row == 1 ? DropWidth() : S(40);

        /// <summary>Width left for a row's label and helper: the busy ring takes room beside the task switch.</summary>
        private int TextWidth(int width, int row) => width - S(32) - ControlWidth(row) - S(12) - (row == TaskRow && _busy ? S(24) : 0);

        private List<string> IntroLines(int width) => Draw.Wrap(Strings.ProactiveIntro, F(FontRole.Body), Math.Max(S(40), width - S(32)));

        private int AsksTop(int width) => S(40) + IntroLines(width).Count * S(20) + S(12);

        private List<RowLayout> Rows(int width)
        {
            AppSettings settings = _page.AppServices.Settings;
            string taskHelp = Elevation.IsElevated ? Strings.ProactiveTaskElevated
                : AppPaths.IsInstalledBuild ? Strings.ProactiveTaskInstalled
                : Strings.ProactiveTaskPortable;
            string? next = settings.RoutineScan == AppSettings.RoutineScanChoices[0] ? null : Strings.ProactiveNext(_page.AppServices.Proactive.NextRoutineUtc);
            (string Label, string Help, string? Extra)[] source =
            [
                (Strings.ProactiveAutoLabel, Strings.ProactiveAutoHelp, null),
                (Strings.ProactiveRoutineLabel, Strings.ProactiveRoutineHelp, next),
                (Strings.ProactiveCleanLabel, Strings.ProactiveCleanHelp, null),
                (Strings.ProactivePerfLabel, Strings.ProactivePerfHelp, null),
                (Strings.ProactiveTaskLabel, taskHelp, null)
            ];
            Font caption = F(FontRole.Caption);
            int top = AsksTop(width) + (_asks.Visible ? S(40) + S(12) : 0);
            List<RowLayout> rows = new(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                List<string> help = Draw.Wrap(source[i].Help, caption, Math.Max(S(40), TextWidth(width, i)));
                if (source[i].Extra is { } extra)
                {
                    help.Add(extra);
                }

                int height = Math.Max(S(RowMinimum), S(20) + help.Count * S(16) + S(16));
                rows.Add(new RowLayout(source[i].Label, help, top, height));
                top += height;
            }

            return rows;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width <= 0)
            {
                return;
            }

            _asks.SetBounds(S(16), AsksTop(Width), Math.Max(0, Width - S(32)), S(40));
            List<RowLayout> rows = Rows(Width);
            for (int i = 0; i < _controls.Length; i++)
            {
                int width = ControlWidth(i);
                int height = i == 1 ? S(32) : S(20);
                _controls[i].SetBounds(Width - S(16) - width, rows[i].Top + (rows[i].Height - height) / 2, width, height);
            }
        }

        protected override void PaintCard(Graphics g)
        {
            ThemeTokens t = T;
            float s = Sc;
            Font body = F(FontRole.Body);
            Font caption = F(FontRole.Caption);
            List<string> intro = IntroLines(Width);
            for (int i = 0; i < intro.Count; i++)
            {
                Draw.Text(g, intro[i], body, 16 * s, (50 + 20 * i) * s, t.Text2);
            }

            List<RowLayout> rows = Rows(Width);
            for (int i = 0; i < rows.Count; i++)
            {
                RowLayout row = rows[i];
                int textWidth = TextWidth(Width, i);
                float top = row.Top + (row.Height - (S(20) + row.Help.Count * S(16))) / 2f;
                Draw.Text(g, row.Label, body, 16 * s, top + 10 * s, t.Text, textWidth);
                for (int j = 0; j < row.Help.Count; j++)
                {
                    Draw.Text(g, row.Help[j], caption, 16 * s, top + (28 + 16 * j) * s, t.Text3, textWidth);
                }

                if (i == TaskRow && _busy)
                {
                    DataGraphics.Ring(g, Width - (16 + 40 + 16) * s, row.Top + row.Height / 2f, 8 * s, Dpi, KitAnimator.Now);
                }

                if (i < rows.Count - 1)
                {
                    RowPaint.Divider(g, 16 * s, Width - 16 * s, row.Top + row.Height, Dpi);
                }
            }
        }
    }
}
