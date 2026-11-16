using DiskMonitor.App;
using DiskMonitor.Core;
using DiskMonitor.Core.Drives;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.UI.Pages;

/// <summary>
/// The event drawer (subplan 05, section 5): what happened, what it means, why it matters, what to do,
/// the Windows text, the history of a combined row, technical details, and the rule actions. It opens at
/// once; nothing in it needs I/O.
/// </summary>
internal sealed class EventDrawer : DrawerPanel
{
    private readonly KitButton _close = new() { Icon = IconName.Close, Variant = ButtonVariant.Subtle, Compact = true, ToolTipText = Strings.DrawerClose, AccessibleName = Strings.DrawerClose };
    private readonly KitButton _primary = new() { Variant = ButtonVariant.Primary };
    private readonly KitButton _viewer = new() { Text = Strings.OpenInEventViewer, Variant = ButtonVariant.Secondary, Icon = IconName.Open };
    private readonly KitButton _more = new() { Icon = IconName.More, Variant = ButtonVariant.Subtle, ToolTipText = Strings.MoreActions, AccessibleName = Strings.MoreActions };
    private readonly KitButton _copy = new() { Text = Strings.CopyDetails, Variant = ButtonVariant.Secondary, Compact = true, Icon = IconName.Copy };
    private readonly KitButton _ignore = new() { Text = Strings.Ignore, Variant = ButtonVariant.Subtle, Icon = IconName.Ignore };
    private readonly KitButton _tier = new() { Text = Strings.ChangeTier, Variant = ButtonVariant.Subtle, ShowChevron = true };
    private readonly Expander _history = new();
    private readonly Expander _technical = new() { Text = Strings.TechnicalDetails };
    private EventGroup? _group;
    private DriveSnapshot _drives = DriveSnapshot.Empty;
    private int _scroll;
    private int _contentHeight;
    private RectangleF _deviceLink;
    private bool _deviceHover;
    private readonly List<(string Label, float Y, int Lines, bool Mono, string Text)> _blocks = [];

    public EventDrawer()
    {
        foreach (KitControl control in new KitControl[] { _close, _primary, _viewer, _more, _copy, _ignore, _tier, _history, _technical })
        {
            control.Backdrop = static t => t.Surface;
            Controls.Add(control);
        }

        _close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _primary.Click += (_, _) => { if (_group is not null) { PrimaryRequested?.Invoke(this, _group); } };
        _viewer.Click += (_, _) => { if (_group is not null) { EventViewer.Open(_group.Latest); } };
        _more.Click += (_, _) => { if (_group is not null) { MoreRequested?.Invoke(this, (_group, _more.RectangleToScreen(new Rectangle(0, _more.Height, 0, 0)).Location)); } };
        _copy.Click += (_, _) => { if (_group is not null) { CopyRequested?.Invoke(this, _group); } };
        _ignore.Click += (_, _) => { if (_group is not null) { IgnoreRequested?.Invoke(this, _group); } };
        _tier.Click += (_, _) => { if (_group is not null) { TierMenuRequested?.Invoke(this, (_group, _tier.RectangleToScreen(new Rectangle(0, _tier.Height, 0, 0)).Location)); } };
        _history.Click += (_, _) => { _history.Expanded = !_history.Expanded; PerformLayout(); Invalidate(); };
        _technical.Click += (_, _) => { _technical.Expanded = !_technical.Expanded; PerformLayout(); Invalidate(); };
        AccessibleRole = AccessibleRole.Pane;
    }

    public event EventHandler? CloseRequested;

    public event EventHandler<EventGroup>? PrimaryRequested;

    public event EventHandler<(EventGroup Group, Point Screen)>? MoreRequested;

    public event EventHandler<EventGroup>? CopyRequested;

    public event EventHandler<EventGroup>? IgnoreRequested;

    public event EventHandler<(EventGroup Group, Point Screen)>? TierMenuRequested;

    public event EventHandler<int>? DriveRequested;

    public EventGroup? Group => _group;

    /// <summary>The recommended action's label: Scan V… for a scannable volume, Open drive for a connected disk, or none.</summary>
    public static string? PrimaryLabel(DiskEvent item, DriveSnapshot drives)
    {
        if (item.Volume is { } volume && drives.FindVolume(volume) is { SupportsChkdsk: true } && (item.SuggestScan || item.Kind == ErrorKind.Logical))
        {
            return Strings.ActionScanVolume(volume);
        }

        return item.DiskNumber is { } disk && drives.FindDisk(disk) is not null ? Strings.RecOpenDrive : null;
    }

    public void Show(EventGroup group, DriveSnapshot drives)
    {
        bool same = _group?.Key == group.Key;
        _group = group;
        _drives = drives;
        if (!same)
        {
            _scroll = 0;
            _history.Expanded = false;
            _technical.Expanded = false;
        }

        DiskEvent item = group.Latest;
        AccessibleName = item.Title;
        string? primary = PrimaryLabel(item, drives);
        _primary.Visible = primary is not null;
        _primary.Text = primary ?? "";
        _viewer.Visible = !item.Synthetic && item.Log is "System" or "Application";
        _history.Visible = group.Count > 1;
        _history.Text = Strings.HistoryRow(group.Count, group.FirstUtc);
        PerformLayout();
        Invalidate(true);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_scroll - e.Delta / 120 * S(48));
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    private void ScrollTo(int value)
    {
        int next = Math.Clamp(value, 0, Math.Max(0, _contentHeight - Height));
        if (next != _scroll)
        {
            _scroll = next;
            PerformLayout();
            Invalidate(true);
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _blocks.Clear();
        if (_group is null || Width <= 0)
        {
            return;
        }

        DiskEvent item = _group.Latest;
        int pad = S(20);
        int width = Width - 2 * pad;
        int y = pad - _scroll;
        _close.SetBounds(Width - pad - S(28), y - S(4), S(28), S(28));
        Font subtitle = F(FontRole.Subtitle);
        int titleLines = Math.Min(2, Draw.Wrap(item.Title, subtitle, width - S(36)).Count);
        y += titleLines * S(24) + S(4);
        y += S(18) + S(18) + S(16);

        int x = pad;
        if (_primary.Visible)
        {
            _primary.SizeToContent();
            _primary.Location = new Point(x, y);
            x += _primary.Width + S(8);
        }

        if (_viewer.Visible)
        {
            _viewer.SizeToContent();
            _viewer.Location = new Point(x, y);
            x += _viewer.Width + S(8);
        }

        _more.SetBounds(x, y, S(32), S(32));
        y += S(32) + S(20);
        y += S(20) + S(20);

        Font body = F(FontRole.Body);
        foreach ((string label, string text) in new[] { (Strings.WhatThisMeans, item.Explanation), (Strings.WhyItMattersLabel, item.WhyItMatters), (Strings.WhatYouCanDo, item.Advice) })
        {
            if (text.Length == 0)
            {
                continue;
            }

            int lines = Draw.Wrap(text, body, width).Count;
            _blocks.Add((label, y + _scroll, lines, false, text));
            y += S(16) + S(4) + lines * S(20) + S(16);
        }

        bool mono = item.Provider is "Chkdsk" or "Microsoft-Windows-Chkdsk" or "Wininit" or "Microsoft-Windows-Wininit";
        Font messageFont = F(mono ? FontRole.Mono : FontRole.Body);
        int messageLines = Math.Max(1, Draw.Wrap(item.Message, messageFont, width - S(16)).Count);
        _blocks.Add((Strings.EventMessage, y + _scroll, messageLines, mono, item.Message));
        y += S(16) + S(4) + S(16) + messageLines * S(mono ? 16 : 20) + S(16);

        if (_history.Visible)
        {
            _history.SetBounds(pad, y, width, S(36));
            y += S(36);
            if (_history.Expanded)
            {
                y += Math.Min(50, _group.Events.Count) * S(28) + S(8);
            }
        }

        _technical.SetBounds(pad, y, width, S(36));
        y += S(36);
        _copy.Visible = _technical.Expanded;
        if (_technical.Expanded)
        {
            y += 6 * S(24) + S(8);
            _copy.SizeToContent();
            _copy.Location = new Point(pad, y);
            y += _copy.Height + S(12);
        }

        y += S(16);
        _ignore.SizeToContent();
        _ignore.Location = new Point(pad, y);
        _tier.SizeToContent();
        _tier.Location = new Point(pad + _ignore.Width + S(8), y);
        y += S(32) + pad;
        _contentHeight = y + _scroll;
    }

    protected override void PaintDrawer(Graphics g)
    {
        if (_group is null)
        {
            return;
        }

        ThemeTokens t = T;
        DiskEvent item = _group.Latest;
        float s = Sc;
        int pad = S(20);
        int width = Width - 2 * pad;
        float y = pad - _scroll;
        Font subtitle = F(FontRole.Subtitle);
        List<string> title = Draw.Wrap(item.Title, subtitle, width - S(36));
        for (int i = 0; i < Math.Min(2, title.Count); i++)
        {
            Draw.Text(g, title[i], subtitle, pad, y + 12 * s + i * 24 * s, t.Text, width - S(36));
        }

        y += Math.Min(2, title.Count) * 24 * s + 4 * s;
        Font caption = F(FontRole.Caption);
        string device = item.DeviceText.Length > 0 ? item.DeviceText : Strings.KindName(item.Kind);
        bool link = item.DiskNumber is { } disk && _drives.FindDisk(disk) is not null;
        Color deviceColor = link ? (_deviceHover ? t.AccentHover : t.Accent) : t.Text2;
        Draw.Text(g, device, caption, pad, y + 9 * s, deviceColor, width);
        int deviceWidth = Math.Min(width, Draw.TextWidth(device, caption));
        _deviceLink = link ? new RectangleF(pad, y, deviceWidth, 18 * s) : RectangleF.Empty;
        if (link && _deviceHover)
        {
            float underline = (float)Math.Floor(y + 16 * s) + 0.5f;
            Draw.Line(g, deviceColor, s, pad, underline, pad + deviceWidth, underline);
        }

        y += 18 * s;
        Draw.Text(g, Format.ExactTime(item.TimeUtc), caption, pad, y + 9 * s, t.Text2, width);
        y += 18 * s + 16 * s + 32 * s + 20 * s;

        float x = pad;
        x += Badges.Tier(g, x, y, item.Tier, Dpi) + 8 * s;
        Badges.Pill(g, x, y, Strings.KindName(item.Kind), Level.Neutral, Dpi);

        Font body = F(FontRole.Body);
        foreach ((string label, float top, int lines, bool mono, string text) in _blocks)
        {
            float by = top - _scroll;
            Draw.Text(g, label, caption, pad, by + 8 * s, t.Text2);
            if (label == Strings.EventMessage)
            {
                Font font = F(mono ? FontRole.Mono : FontRole.Body);
                int lineHeight = S(mono ? 16 : 20);
                RectangleF inset = new(pad, by + 20 * s, width, lines * lineHeight + 16 * s);
                Draw.FillRounded(g, inset, 4 * s, t.Bg);
                List<string> wrapped = Draw.Wrap(text, font, width - S(16));
                for (int i = 0; i < wrapped.Count; i++)
                {
                    Draw.Text(g, wrapped[i], font, pad + 8 * s, inset.Y + 8 * s + i * lineHeight + lineHeight / 2f, t.Text, width - S(16));
                }
            }
            else
            {
                List<string> wrapped = Draw.Wrap(text, body, width);
                for (int i = 0; i < wrapped.Count; i++)
                {
                    Draw.Text(g, wrapped[i], body, pad, by + 20 * s + i * 20 * s + 10 * s, t.Text, width);
                }
            }
        }

        if (_history.Visible && _history.Expanded)
        {
            float top = _history.Bottom;
            List<DiskEvent> latest = _group.Events.Take(50).ToList();
            for (int i = 0; i < latest.Count; i++)
            {
                float cy = top + 14 * s + i * 28 * s;
                DataGraphics.TimelineEntry(g, pad + 8 * s, cy, pad + 24 * s, Width - pad, latest[i].DeviceText.Length > 0 ? latest[i].DeviceText : Strings.KindName(latest[i].Kind), Format.RecentDate(latest[i].TimeUtc, inSentence: false), i == 0, i == latest.Count - 1, 28 * s, Dpi);
            }
        }

        if (_technical.Expanded)
        {
            float top = _technical.Bottom + 4 * s;
            (string Label, string Value)[] rows =
            [
                (Strings.TechSource, item.Provider),
                (Strings.TechEventId, item.Synthetic ? Strings.Dash : item.EventId.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                (Strings.TechLog, item.Log),
                (Strings.TechLevel, item.Synthetic ? Strings.Dash : Strings.LevelName(item.Level)),
                (Strings.TechRecordId, item.Synthetic ? Strings.Dash : item.RecordId.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                (Strings.TechRule, item.RuleId)
            ];
            for (int i = 0; i < rows.Length; i++)
            {
                float cy = top + 12 * s + i * 24 * s;
                Draw.Text(g, rows[i].Label, caption, pad, cy, t.Text3);
                Draw.Text(g, rows[i].Value, body, pad + 96 * s, cy, t.Text, width - S(96));
            }
        }

        RowPaint.Scrollbar(g, new RectangleF(0, 0, Width, Height), Height, _contentHeight, _scroll, false, Dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = _deviceLink.Contains(e.Location);
        if (hover != _deviceHover)
        {
            _deviceHover = hover;
            Cursor = hover ? Cursors.Hand : Cursor;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_deviceLink.Contains(e.Location) && _group?.Latest.DiskNumber is { } disk)
        {
            DriveRequested?.Invoke(this, disk);
        }
    }

    /// <summary>A collapsible row: chevron, label; Enter or a click toggles it.</summary>
    private sealed class Expander : InteractiveKitControl
    {
        public bool Expanded { get; set; }

        public Expander() => AccessibleRole = AccessibleRole.OutlineButton;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => base.Text;
            set { base.Text = value; AccessibleName = value; Invalidate(); }
        }

        protected override void PaintContent(Graphics g)
        {
            RectangleF r = ClientRectangle;
            if (ShowHover)
            {
                Draw.FillRounded(g, r, Sf(4), T.Hover);
            }

            float line = Sf(0.5f);
            Draw.Line(g, T.Stroke, Sc, 0, line, Width, line);
            Draw.Icon(g, Expanded ? IconName.ChevronDown : IconName.ChevronRight, 0, (Height - S(16)) / 2f, 16, Dpi, T.Text2);
            Draw.Text(g, Text, F(FontRole.BodyStrong), S(24), Height / 2f, T.Text, Width - S(24));
        }
    }
}

/// <summary>Opens Event Viewer on an event's log (it must be started through the shell).</summary>
internal static class EventViewer
{
    public static void Open(DiskEvent item)
    {
        string arguments = $"/c:\"{item.Log}\" /f:\"*[System[EventRecordID={item.RecordId}]]\"";
        try
        {
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("eventvwr.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Diagnostics.ErrorLog.Write("Event Viewer", ex);
        }
    }
}
