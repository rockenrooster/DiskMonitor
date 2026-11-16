using DiskMonitor.App;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>
/// Settings, Rules (subplan 08): change how serious each kind of event is, or turn it off. Changes go
/// through <see cref="AppServices.SetRuleOverride"/> and apply at once to the whole history.
/// </summary>
internal sealed class RulesSection : SettingsSectionView
{
    private const int RowHeight = 64;
    private const int InfoHeight = 40 + 12;

    private readonly SearchBox _filter = new() { Placeholder = Strings.FilterRules, AccessibleName = Strings.FilterRules };
    private readonly Chip _all = new() { Text = Strings.ChipAll, AutoToggle = false, On = true };
    private readonly Chip[] _kindChips;
    private readonly KitButton _resetAll = new() { Text = Strings.ResetAll, Variant = ButtonVariant.Subtle, Compact = true, DisabledReason = Strings.NothingChangedYet };
    private readonly RulesCard _card;
    private readonly List<RuleRow> _rows = [];
    private ErrorKind? _kind;
    private string? _infoRule;
    private string _infoText = "";
    private string? _pendingBack;

    public RulesSection(SettingsPage page) : base(page, "rules", Strings.SectionRules, Strings.IntroRules)
    {
        _kindChips = TierExtensions.Kinds.Select(kind => new Chip { Text = Strings.KindName(kind), AutoToggle = false }).ToArray();
        _card = new RulesCard(this);
        foreach (KitControl control in new KitControl[] { _filter, _all, _resetAll }.Concat(_kindChips))
        {
            control.Backdrop = static t => t.Bg;
        }

        Controls.AddRange([_filter, _all, .. _kindChips, _resetAll, _card]);
        _filter.SearchChanged += (_, _) => Page.Relayout();
        _filter.Dismissed += (_, _) => _card.Focus();
        _all.Toggled += (_, _) => SetKind(null);
        for (int i = 0; i < _kindChips.Length; i++)
        {
            ErrorKind kind = TierExtensions.Kinds[i];
            _kindChips[i].Toggled += (_, _) => SetKind(_kind == kind ? null : kind);
        }

        _resetAll.Click += (_, _) => ConfirmResetAll();
    }

    /// <summary>One rule: the catalog's, or an override for an id the catalog no longer has.</summary>
    private sealed class RuleRow
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required ErrorKind Kind { get; init; }
        public required string Source { get; init; }
        public required bool DefaultOn { get; init; }
        public required Tier DefaultTier { get; init; }
        public required KitSwitch Switch { get; init; }
        public required DropDown TierDrop { get; init; }
        public required KitButton Reset { get; init; }
        public bool On { get; set; }
        public Tier Current { get; set; }

        public bool Changed => On != DefaultOn || (On && Current != DefaultTier);

        public string? ValueFor(bool on, Tier tier) => OverrideFor(DefaultOn, DefaultTier, on, tier);
    }

    /// <summary>Harness hook: the info bar under a rule, as after a change.</summary>
    internal void PreviewInfo(string id, string text, string? filter)
    {
        if (filter is not null)
        {
            _filter.Text = filter;
        }

        SetInfo(id, text);
    }

    public bool FocusFilter()
    {
        _filter.Focus();
        return true;
    }

    public override void Reload() => Load(() =>
    {
        IReadOnlyDictionary<string, string> overrides = Services.Settings.RuleOverrides;
        HashSet<string> wanted = new(RuleCatalog.All.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        wanted.UnionWith(overrides.Keys);
        foreach (RuleRow gone in _rows.Where(r => !wanted.Contains(r.Id)).ToList())
        {
            _card.Controls.Remove(gone.Switch);
            _card.Controls.Remove(gone.TierDrop);
            _card.Controls.Remove(gone.Reset);
            gone.Switch.Dispose();
            gone.TierDrop.Dispose();
            gone.Reset.Dispose();
            _rows.Remove(gone);
        }

        foreach (EventRule rule in RuleCatalog.All)
        {
            if (_rows.All(r => !r.Id.Equals(rule.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _rows.Add(CreateRow(rule.Id, rule.Title, rule.Kind, rule.Source, !rule.Ignore, rule.Tier));
            }
        }

        foreach (string id in overrides.Keys)
        {
            if (_rows.All(r => !r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                _rows.Add(CreateRow(id, Strings.UnrecognisedRule(id), ErrorKind.Other, "", false, Tier.Low));
            }
        }

        _rows.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture));
        foreach (RuleRow row in _rows)
        {
            (row.On, row.Current) = State(row, overrides);
            row.Switch.Checked = row.On;
            row.TierDrop.SelectedIndex = Array.IndexOf(TierExtensions.Descending, row.Current);
            row.TierDrop.Enabled = row.On;
            row.Reset.Visible = row.Changed;
        }

        _resetAll.Enabled = overrides.Count > 0;
        _all.On = _kind is null;
        for (int i = 0; i < _kindChips.Length; i++)
        {
            _kindChips[i].On = _kind == TierExtensions.Kinds[i];
        }
    });

    public override void OnRefresh(RefreshReason reasons)
    {
        if ((reasons & RefreshReason.Settings) != 0)
        {
            Reload();
        }

        // "n events are back" is only known once the history has been read again.
        // The store changes while the reload still runs; the monitor's status change follows when it ends.
        if (_pendingBack is { } id && (reasons & (RefreshReason.Events | RefreshReason.Monitor)) != 0 && !Services.Monitor.IsLoading)
        {
            _pendingBack = null;
            SetInfo(id, Strings.RuleBack(CountEvents(id)));
        }
    }

    private static (bool On, Tier Tier) State(RuleRow row, IReadOnlyDictionary<string, string> overrides) =>
        StateFor(row.DefaultOn, row.DefaultTier, overrides.TryGetValue(row.Id, out string? value) ? value : null);

    /// <summary>The settings value for a rule's switch and tier: null when both equal the rule's default.</summary>
    internal static string? OverrideFor(bool defaultOn, Tier defaultTier, bool on, Tier tier) =>
        !on ? (defaultOn ? RuleCatalog.IgnoreValue : null)
        : defaultOn && tier == defaultTier ? null
        : tier.ToString();

    /// <summary>The switch and tier a rule shows for its override (an unknown value shows the default).</summary>
    internal static (bool On, Tier Tier) StateFor(bool defaultOn, Tier defaultTier, string? value)
    {
        if (value is not null)
        {
            if (value.Equals(RuleCatalog.IgnoreValue, StringComparison.OrdinalIgnoreCase))
            {
                return (false, defaultTier);
            }

            if (Enum.TryParse(value, ignoreCase: true, out Tier chosen) && Enum.IsDefined(chosen))
            {
                return (true, chosen);
            }
        }

        return (defaultOn, defaultTier);
    }

    private RuleRow CreateRow(string id, string title, ErrorKind kind, string source, bool defaultOn, Tier defaultTier)
    {
        RuleRow row = new()
        {
            Id = id,
            Title = title,
            Kind = kind,
            Source = source,
            DefaultOn = defaultOn,
            DefaultTier = defaultTier,
            Switch = new KitSwitch { AccessibleName = title, Backdrop = static t => t.Surface },
            TierDrop = new DropDown { Backdrop = static t => t.Surface, AccessibleDescription = title, MarkChanged = false },
            Reset = new KitButton { Text = Strings.Reset, Variant = ButtonVariant.Subtle, Compact = true, Backdrop = static t => t.Surface, AccessibleName = title + Strings.Separator + Strings.Reset }
        };
        row.TierDrop.SetItems(TierExtensions.Descending.Select(Strings.TierName));
        row.Switch.CheckedChanged += (_, _) => { if (!Loading) { Apply(row, row.Switch.Checked, row.Current); } };
        row.TierDrop.SelectionChanged += (_, _) =>
        {
            if (!Loading && row.TierDrop.SelectedIndex >= 0)
            {
                Apply(row, row.On, TierExtensions.Descending[row.TierDrop.SelectedIndex]);
            }
        };
        row.Reset.Click += (_, _) => Apply(row, row.DefaultOn, row.DefaultTier);
        _card.Controls.AddRange([row.Switch, row.TierDrop, row.Reset]);
        return row;
    }

    private int CountEvents(string id) => Services.Events.Snapshot().Count(e => e.RuleId.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies a switch and tier to a rule, then says under the row how many events it moved.</summary>
    private void Apply(RuleRow row, bool on, Tier tier)
    {
        bool wasOn = row.On;
        Tier wasTier = row.Current;
        if (wasOn == on && (!on || wasTier == tier))
        {
            return;
        }

        int before = CountEvents(row.Id);
        _pendingBack = null;
        Services.SetRuleOverride(row.Id, row.ValueFor(on, tier));
        if (!on)
        {
            SetInfo(row.Id, Strings.RuleHidden(before));
        }
        else if (!wasOn)
        {
            if (Services.DemoMode)
            {
                SetInfo(row.Id, Strings.RuleBack(0));
            }
            else
            {
                // Shown when the reloaded history arrives (OnRefresh).
                _pendingBack = row.Id;
                SetInfo(null, "");
            }
        }
        else
        {
            SetInfo(row.Id, Strings.RuleChangedTier(before));
        }

        Reload();
        Page.Relayout();
    }

    private void SetInfo(string? id, string text)
    {
        _infoRule = id;
        _infoText = text;
        Page.Relayout();
        _card.Invalidate();
    }

    private void SetKind(ErrorKind? kind)
    {
        _kind = kind;
        _all.On = kind is null;
        for (int i = 0; i < _kindChips.Length; i++)
        {
            _kindChips[i].On = kind == TierExtensions.Kinds[i];
        }

        Page.Relayout();
    }

    private void ConfirmResetAll()
    {
        int count = Services.Settings.RuleOverrides.Count;
        using KitDialog dialog = new KitDialog(Strings.ResetAllTitle, 480, cancelResult: 0)
            .Paragraph(Strings.ResetAllText(count))
            .Button(new DialogButton(Strings.Cancel, ButtonVariant.Secondary, 0, InitialFocus: true))
            .Button(new DialogButton(Strings.ResetRules, ButtonVariant.Primary, 1));
        if (dialog.ShowOver(Page.Nav.Window) != 1)
        {
            return;
        }

        Services.Settings.RuleOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Services.SaveAndReloadEvents();
        _pendingBack = null;
        SetInfo(null, "");
        Reload();
    }

    private bool Matches(RuleRow row)
    {
        if (_kind is { } kind && row.Kind != kind)
        {
            return false;
        }

        string query = _filter.Text.Trim();
        return query.Length == 0
            || row.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private IEnumerable<RuleRow> VisibleRows => _rows.Where(Matches);

    protected override int ArrangeContent(int width, int top)
    {
        // Toolbar: the search box, the kind chips (wrapping under it when they do not fit), Reset all at the right.
        _resetAll.SizeToContent();
        _resetAll.Location = new Point(width - _resetAll.Width, top + (S(32) - _resetAll.Height) / 2);
        // The filter box narrows (down to 200 px) before a chip has to wrap under it.
        Chip[] chips = [_all, .. _kindChips];
        int chipsWidth = chips.Sum(c => c.PreferredWidth) + (chips.Length - 1) * S(8);
        int room = width - _resetAll.Width - S(16) - S(16) - chipsWidth;
        int searchWidth = Math.Clamp(room, S(200), S(320));
        searchWidth = Math.Min(searchWidth, width - _resetAll.Width - S(16));
        _filter.SetBounds(0, top, searchWidth, S(32));
        int x = searchWidth + S(16);
        int lineTop = top;
        int limit = width - _resetAll.Width - S(16);
        foreach (Chip chip in chips)
        {
            int chipWidth = chip.PreferredWidth;
            if (x + chipWidth > limit && x > 0)
            {
                x = 0;
                lineTop += S(32) + S(8);
                limit = width;
            }

            chip.SetBounds(x, lineTop + S(2), chipWidth, S(28));
            x += chipWidth + S(8);
        }

        int cardTop = lineTop + S(32) + S(16);
        int height = _card.Measure();
        _card.SetBounds(0, cardTop, width, height);
        _card.PerformLayout();
        _card.Invalidate();
        return cardTop + height;
    }

    /// <summary>The rule rows: 64 px each, plus the Low info bar under the row last changed.</summary>
    private sealed class RulesCard(RulesSection section) : Card
    {
        private int RowHeightOf(RuleRow row) => S(RowHeight) + (row.Id == section._infoRule && section._infoText.Length > 0 ? S(InfoHeight) : 0);

        public int Measure()
        {
            int total = 0;
            foreach (RuleRow row in section.VisibleRows)
            {
                total += RowHeightOf(row);
            }

            return Math.Max(total, S(RowHeight));
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            Backdrop = static t => t.Bg;
        }

        private int ControlsLeft(RuleRow row)
        {
            int x = Width - S(16) - DropWidth(row.TierDrop);
            if (row.Changed)
            {
                x -= S(8) + row.Reset.PreferredWidth + S(8) + Draw.TextWidth(Strings.DefaultIs(row.DefaultOn ? row.DefaultTier : null), F(FontRole.Caption));
            }

            return x;
        }

        private int DropWidth(DropDown drop) => drop.Items.Max(item => Draw.TextWidth(item, F(FontRole.Body))) + S(12 + 8 + 26);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            HashSet<RuleRow> shown = [.. section.VisibleRows];
            int y = 0;
            foreach (RuleRow row in section._rows)
            {
                bool visible = shown.Contains(row);
                row.Switch.Visible = visible;
                row.TierDrop.Visible = visible;
                row.Reset.Visible = visible && row.Changed;
                if (!visible)
                {
                    continue;
                }

                int centre = y + S(RowHeight) / 2;
                row.Switch.SetBounds(S(16), centre - S(10), S(40), S(20));
                int dropWidth = DropWidth(row.TierDrop);
                row.TierDrop.SetBounds(Width - S(16) - dropWidth, centre - S(16), dropWidth, S(32));
                if (row.Changed)
                {
                    row.Reset.SizeToContent();
                    row.Reset.Location = new Point(row.TierDrop.Left - S(8) - row.Reset.Width, centre - row.Reset.Height / 2);
                }

                y += RowHeightOf(row);
            }
        }

        protected override void PaintCard(Graphics g)
        {
            ThemeTokens t = T;
            float s = Sc;
            Font strong = F(FontRole.BodyStrong);
            Font caption = F(FontRole.Caption);
            int y = 0;
            bool first = true;
            foreach (RuleRow row in section.VisibleRows)
            {
                if (!first)
                {
                    float line = (float)Math.Floor((double)y) + 0.5f;
                    Draw.Line(g, t.Stroke, s, 16 * s, line, Width - 16 * s, line);
                }

                first = false;
                int textWidth = ControlsLeft(row) - S(68) - S(16);
                Draw.Text(g, row.Title, strong, 68 * s, y + 23 * s, row.On ? t.Text : t.Text2, textWidth);
                Draw.Text(g, Strings.RuleMeta(row.Id, row.Kind, row.Source), caption, 68 * s, y + 43 * s, t.Text3, textWidth);
                if (row.Changed)
                {
                    string text = Strings.DefaultIs(row.DefaultOn ? row.DefaultTier : null);
                    Draw.TextRight(g, text, caption, row.Reset.Left - 8 * s, y + 32 * s, t.Text3);
                }

                if (row.Id == section._infoRule && section._infoText.Length > 0)
                {
                    Surfaces.InfoBar(g, new RectangleF(16 * s, y + RowHeight * s, Width - 32 * s, 40 * s), Level.Low, section._infoText, null, Dpi);
                }

                y += RowHeightOf(row);
            }

            if (first)
            {
                Draw.TextCentered(g, Strings.EmptyFilteredTitle, F(FontRole.Body), Width / 2f, RowHeight * s / 2f, t.Text2);
            }
        }
    }
}
