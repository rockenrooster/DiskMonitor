using System.Drawing.Drawing2D;
using DiskMonitor.Core.Events;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor.Diagnostics;

/// <summary>
/// MediaVerifier's reference sheets reproduced with Disk Monitor's kit: the same layout and the same sample
/// labels (read from docs/plans/design/mediaverifier/*.svg), every state forced with the preview hook. The
/// labels here are fixtures, not UI strings. Coordinates are the SVGs' logical pixels.
/// </summary>
internal static class ReferenceSheets
{
    private static ThemeTokens T => ThemeService.Tokens;

    private static Font F(FontRole role, int dpi) => ThemeService.Resources.Font(role, dpi);

    // ---------------------------------------------------------------------------------------------
    // Components and states (1280 × 1434)
    // ---------------------------------------------------------------------------------------------

    public static Bitmap Components(int dpi)
    {
        SheetCanvas c = new(dpi);
        float s = dpi / 96f;
        void Label(string text, float x, float y, FontRole role, Func<ThemeTokens, Color> color) =>
            c.Draw((g, k) => Draw.Text(g, text, F(role, dpi), x * k, y * k, color(T)));

        Label("Components and states", 24, 28, FontRole.Title, t => t.Text);

        // Buttons
        Label("BUTTONS", 24, 64, FontRole.CaptionStrong, t => t.Text3);
        string[] columns = ["REST", "HOVER", "PRESSED", "DISABLED", "FOCUS"];
        float[] xs = [200, 360, 520, 680, 840];
        PreviewState[] states = [PreviewState.None, PreviewState.Hover, PreviewState.Pressed, PreviewState.Disabled, PreviewState.Focus];
        for (int i = 0; i < columns.Length; i++)
        {
            Label(columns[i], xs[i], 64, FontRole.CaptionStrong, t => t.Text3);
        }

        (string Name, ButtonVariant Variant, float Y)[] rows =
        [
            ("Primary", ButtonVariant.Primary, 84), ("Secondary", ButtonVariant.Secondary, 132),
            ("Subtle", ButtonVariant.Subtle, 180), ("Danger", ButtonVariant.Danger, 228)
        ];
        foreach ((string name, ButtonVariant variant, float y) in rows)
        {
            Label(name, 24, y + 16, FontRole.Body, t => t.Text2);
            for (int i = 0; i < 5; i++)
            {
                AddButton(c, "Scan folder", variant, IconName.Play, xs[i], y, false, states[i], dpi);
            }
        }

        Label("Icon button", 24, 292, FontRole.Body, t => t.Text2);
        for (int i = 0; i < 5; i++)
        {
            KitButton icon = new() { Icon = IconName.Refresh, Variant = ButtonVariant.Subtle, Preview = states[i], AccessibleName = "Refresh" };
            c.Add(icon, xs[i], 276, 32, 32);
        }

        Label("Compact (28)", 24, 340, FontRole.Body, t => t.Text2);
        for (int i = 0; i < 4; i++)
        {
            AddButton(c, "Details", ButtonVariant.Secondary, null, xs[i], 326, true, states[i], dpi);
        }

        Label("Split button", 24, 388, FontRole.Body, t => t.Text2);
        for (int i = 0; i < 3; i++)
        {
            SplitButton split = new() { Text = "Scan", Preview = states[i] };
            c.Add(split, xs[i], 372, split.PreferredWidth / s, 32);
        }

        // Severity
        Label("SEVERITY", 24, 428, FontRole.CaptionStrong, t => t.Text3);
        c.Draw((g, k) =>
        {
            float x = 24;
            foreach ((Level level, string word) in new[] { (Level.Critical, "Critical"), (Level.High, "High"), (Level.Medium, "Medium"), (Level.Low, "Low"), (Level.Ok, "OK") })
            {
                int width = Badges.Severity(g, x * k, 444 * k, level, word, dpi);
                x += width / k + 12;
            }

            Level[] levels = [Level.Critical, Level.High, Level.Medium, Level.Low, Level.Ok];
            for (int i = 0; i < 5; i++)
            {
                Draw.Severity(g, levels[i], (393 + 22 * i) * k, 448 * k, 12, dpi, T.LevelColors(levels[i]).Text, T.Bg);
                Draw.Severity(g, levels[i], (515 + 28 * i) * k, 446 * k, 16, dpi, T.LevelColors(levels[i]).Text, T.Bg);
                Draw.Severity(g, levels[i], (667 + 36 * i) * k, 442 * k, 24, dpi, T.LevelColors(levels[i]).Text, T.Bg);
            }

            (StateIcon State, string Word, float X)[] stateIcons =
            [
                (StateIcon.Pending, "Pending", 863), (StateIcon.Skipped, "Skipped", 939.94f),
                (StateIcon.Missing, "Missing", 1016.88f), (StateIcon.Error, "Error", 1093.38f)
            ];
            foreach ((StateIcon state, string word, float x0) in stateIcons)
            {
                Draw.State(g, state, x0 * k, 446 * k, 16, dpi, T.Text3);
                Draw.Text(g, word, F(FontRole.Caption, dpi), (x0 + 22) * k, 454 * k, T.Text3);
            }
        });

        // Chips and pills
        Label("CHIPS AND PILLS", 24, 488, FontRole.CaptionStrong, t => t.Text3);
        AddChip(c, "All", "325", null, null, true, PreviewState.None, 24, dpi);
        AddChip(c, "Critical", "3", Level.Critical, null, false, PreviewState.None, 98, dpi);
        AddChip(c, "High", "14", Level.High, null, false, PreviewState.Hover, 204, dpi);
        AddChip(c, "Medium", "96", Level.Medium, null, true, PreviewState.None, 301, dpi);
        AddChip(c, "Photos", null, null, IconName.Photo, false, PreviewState.None, 419, dpi);
        AddChip(c, "Music", null, null, IconName.Music, true, PreviewState.None, 511, dpi);
        c.Draw((g, k) =>
        {
            float x = 614;
            foreach ((string text, Level level) in new[] { ("Library", Level.Neutral), ("Likely", Level.Neutral), ("Completed", Level.Ok), ("Stopped", Level.Medium), ("Could not run", Level.Critical) })
            {
                int width = Badges.Pill(g, x * k, 506 * k, text, level, dpi);
                x += width / k + 8;
            }
        });

        // Inputs
        Label("INPUTS", 24, 554, FontRole.CaptionStrong, t => t.Text3);
        Label("REST", 200, 554, FontRole.CaptionStrong, t => t.Text3);
        Label("HOVER", 420, 554, FontRole.CaptionStrong, t => t.Text3);
        Label("FOCUS", 640, 554, FontRole.CaptionStrong, t => t.Text3);
        Label("DISABLED", 860, 554, FontRole.CaptionStrong, t => t.Text3);
        Label("Text box", 24, 586, FontRole.Body, t => t.Text2);
        Label("Search box", 24, 630, FontRole.Body, t => t.Text2);
        Label("Drop-down", 24, 674, FontRole.Body, t => t.Text2);
        Label("Segmented", 24, 718, FontRole.Body, t => t.Text2);
        Label("Switch, check, radio", 24, 756, FontRole.Body, t => t.Text2);
        c.Draw((g, k) =>
        {
            Rectangle Box(float x, float y) => new((int)Math.Round(x * k), (int)Math.Round(y * k), (int)Math.Round(200 * k), (int)Math.Round(32 * k));
            KitTextBox.RenderBox(g, Box(200, 570), "Weekly check", "", false, false, false, false, false, dpi);
            KitTextBox.RenderBox(g, Box(420, 570), "Weekly check", "", false, true, false, false, false, dpi);
            KitTextBox.RenderBox(g, Box(640, 570), "Weekly check", "", true, false, false, false, false, dpi);
            KitTextBox.RenderBox(g, Box(860, 570), "Weekly check", "", false, false, true, false, false, dpi);
            KitTextBox.RenderBox(g, Box(200, 614), "", "Search files", false, false, false, false, true, dpi);
            KitTextBox.RenderBox(g, Box(420, 614), "", "Search files", false, true, false, false, true, dpi);
            KitTextBox.RenderBox(g, Box(640, 614), "overpass", "Search files", true, false, false, false, true, dpi);
            DropDown.Render(g, Box(200, 658), "All folders", false, false, false, false, false, false, dpi);
            DropDown.Render(g, Box(420, 658), "All folders", true, false, false, false, false, false, dpi);
            DropDown.Render(g, Box(640, 658), "All folders", false, true, false, false, false, false, dpi);
        });
        SegmentedControl first = new();
        first.SetItems(["Quick", "Standard", "Deep"], 1);
        c.Add(first, 200, 702, first.PreferredWidth / s, 32);
        SegmentedControl second = new();
        second.SetItems(["Severity", "Folder", "Rule"], 0);
        c.Add(second, 480, 702, second.PreferredWidth / s, 32);

        c.Add(new KitSwitch { Checked = false }, 200, 746, 40, 20);
        c.Add(new KitSwitch { Checked = false, Preview = PreviewState.Hover }, 256, 746, 40, 20);
        c.Add(new KitSwitch { Checked = true }, 312, 746, 40, 20);
        c.Add(new KitSwitch { Checked = true, Preview = PreviewState.Hover }, 368, 746, 40, 20);
        c.Add(new KitCheckBox { Checked = false }, 440, 747, 18, 18);
        c.Add(new KitCheckBox { Checked = false, Preview = PreviewState.Hover }, 472, 747, 18, 18);
        c.Add(new KitCheckBox { Checked = true }, 504, 747, 18, 18);
        c.Add(new KitCheckBox { Checked = true, Preview = PreviewState.Hover }, 536, 747, 18, 18);
        c.Draw((g, k) =>
        {
            Radio.Render(g, 609 * k, 756 * k, false, false, dpi);
            Radio.Render(g, 641 * k, 756 * k, true, false, dpi);
        });
        c.Add(new KitSwitch { Checked = true, Preview = PreviewState.Focus }, 700, 746, 40, 20);
        c.Add(new KitCheckBox { Checked = true, Preview = PreviewState.Focus }, 772, 747, 18, 18);

        // Progress and data
        Label("PROGRESS AND DATA", 24, 798, FontRole.CaptionStrong, t => t.Text3);
        c.Draw((g, k) =>
        {
            DataGraphics.ProgressBar(g, new RectangleF(24 * k, 818 * k, 280 * k, 8 * k), 0.42, 0);
            DataGraphics.ProgressBar(g, new RectangleF(24 * k, 836 * k, 280 * k, 4 * k), 0.42, 0);
            Draw.ProgressRing(g, 336 * k, 826 * k, 7 * k, 2 * k, -60, T.Accent);
            Draw.ProgressRing(g, 368 * k, 826 * k, 9 * k, 2 * k, -60, T.Accent);
            float x = 408;
            foreach ((float width, Color color) in new[] { (3.962f, T.CritFill), (4.677f, T.HighFill), (32.073f, T.MedFill), (70.829f, T.LowFill), (200.459f, T.OkFill) })
            {
                Draw.FillRounded(g, new RectangleF(x * k, 820 * k, width * k, 8 * k), 2 * k, color);
                x += width + 2;
            }

            DataGraphics.Donut(g, 784 * k, 828 * k, 22 * k, 6 * k, 97.89 / 119.38, T.Accent);
            DataGraphics.Donut(g, 844 * k, 828 * k, 22 * k, 6 * k, 117.83 / 119.38, T.OkFill);
            float[] ys = [845.0f, 836.7f, 826.8f, 830.1f, 820.1f, 811.0f, 813.5f, 821.0f, 817.6f, 811.8f, 815.1f, 819.3f, 822.6f, 821.0f, 824.3f];
            PointF[] points = ys.Select((y, i) => new PointF((896 + i * 200 / 14f) * k, y * k)).ToArray();
            using GraphicsPath area = new();
            area.AddLines(points);
            area.AddLine(points[^1], new PointF(1096 * k, 848 * k));
            area.AddLine(new PointF(1096 * k, 848 * k), new PointF(896 * k, 848 * k));
            area.CloseFigure();
            g.FillPath(ThemeService.Resources.Brush(Color.FromArgb(36, T.Accent)), area);
            g.DrawLines(ThemeService.Resources.Pen(T.Accent, 1.5f * k, round: true), points);
            Draw.Circle(g, T.Accent, points[^1].X, points[^1].Y, 2.5f * k);
        });

        // Info bars
        Label("INFO BARS", 24, 874, FontRole.CaptionStrong, t => t.Text3);
        c.Draw((g, k) =>
        {
            Surfaces.InfoBar(g, new RectangleF(24 * k, 888 * k, 600 * k, 40 * k), Level.Medium, "2 changed videos will be skipped.", "Show", dpi);
            Surfaces.InfoBar(g, new RectangleF(640 * k, 888 * k, 600 * k, 40 * k), Level.Critical, "Drive D: returned read errors on 4 files.", "What to do", dpi);
            Surfaces.InfoBar(g, new RectangleF(24 * k, 936 * k, 600 * k, 40 * k), Level.Low, "Keep your library checked automatically.", "Create schedule", dpi);
            Surfaces.InfoBar(g, new RectangleF(640 * k, 936 * k, 600 * k, 40 * k), Level.Ok, "Restored 3 files from Backup (E:).", "Undo", dpi);
        });

        // List rows
        Label("LIST ROWS: GROUP HEADER 32 PX, TWO-LINE ROW 44 PX (REST, HOVER, SELECTED)", 24, 996, FontRole.CaptionStrong, t => t.Text3);
        c.Draw((g, k) =>
        {
            Draw.FillRounded(g, new RectangleF(24 * k, 1010 * k, 1232 * k, 164 * k), 8 * k, T.Surface);
            Draw.StrokeRounded(g, new RectangleF(24 * k, 1010 * k, 1232 * k, 164 * k), 8 * k, T.Stroke, k);
            RowPaint.GroupHeader(g, new RectangleF(24 * k, 1010 * k, 1232 * k, 32 * k), false, Level.High, null, "High", "14", dpi);
            TwoLine(g, k, dpi, 1042, "Night Drive - 07 - Overpass.flac", "Likely made from a lossy source  ·  hard cutoff at 16.6 kHz", "31.2 MB", @"D:\Music\Night Drive\Overpass", false, false);
            TwoLine(g, k, dpi, 1086, "Coastline 4K.mkv", "Likely upscaled  ·  2160p file with detail equivalent to 1080p", "", @"D:\Videos\Travel", true, false);
            TwoLine(g, k, dpi, 1130, "Sunday Session - 02.flac", "Embedded checksum mismatch  ·  audio differs from the stored MD5", "41.0 MB", @"D:\Music\Sunday Session", false, true);
        });

        // Navigation, scrollbar, breadcrumb
        Label("NAVIGATION ITEMS 40 PX (REST, HOVER, SELECTED)   ·   SCROLLBAR (REST, HOVER)   ·   BREADCRUMB", 24, 1198, FontRole.CaptionStrong, t => t.Text3);
        c.Draw((g, k) =>
        {
            RowPaint.NavItem(g, new RectangleF(24 * k, 1212 * k, 184 * k, 40 * k), IconName.Library, "Rest", false, false, false, dpi);
            RowPaint.NavItem(g, new RectangleF(224 * k, 1212 * k, 184 * k, 40 * k), IconName.Library, "Hover", true, false, false, dpi);
            RowPaint.NavItem(g, new RectangleF(424 * k, 1212 * k, 184 * k, 40 * k), IconName.Library, "Selected", false, true, false, dpi);
            int pill = Badges.PillWidth("128", dpi);
            Badges.Pill(g, 598 * k - pill, 1222 * k, "128", Level.Critical, dpi, centered: true);
            Draw.FillRounded(g, new RectangleF(660 * k, 1212 * k, 6 * k, 40 * k), 3 * k, Color.FromArgb(153, T.StrokeStrong));
            Draw.FillRounded(g, new RectangleF(684 * k, 1212 * k, 10 * k, 40 * k), 5 * k, T.StrokeStrong);
            Font body = F(FontRole.Body, dpi);
            float x = 740 * k;
            string[] crumbs = ["Music", "Night Drive", "Overpass"];
            for (int i = 0; i < crumbs.Length; i++)
            {
                Draw.Text(g, crumbs[i], body, x, 1232 * k, i == crumbs.Length - 1 ? T.Text : T.Text2);
                x += Draw.TextWidth(crumbs[i], body);
                if (i < crumbs.Length - 1)
                {
                    Draw.Icon(g, IconName.ChevronRight, x + 4 * k, 1224 * k, 16, dpi, T.Text3);
                    x += 24 * k;
                }
            }
        });

        // Card, skeleton, empty state
        Label("CARD   ·   LOADING SKELETON   ·   EMPTY STATE", 24, 1276, FontRole.CaptionStrong, t => t.Text3);
        c.Draw((g, k) =>
        {
            Surfaces.Card(g, new RectangleF(24 * k, 1290 * k, 300 * k, 120 * k), "CARD TITLE", dpi);
            Draw.Text(g, "Card body text", F(FontRole.Body, dpi), 40 * k, 1342 * k, T.Text);
            Draw.Text(g, "Secondary line", F(FontRole.Caption, dpi), 40 * k, 1364 * k, T.Text2);
            Surfaces.Card(g, new RectangleF(340 * k, 1290 * k, 300 * k, 120 * k), null, dpi);
            Surfaces.SkeletonRow(g, 356 * k, 1306 * k, 150 * k, 220 * k, dpi);
            Surfaces.SkeletonRow(g, 356 * k, 1340 * k, 126 * k, 190 * k, dpi);
            Surfaces.SkeletonRow(g, 356 * k, 1374 * k, 102 * k, 160 * k, dpi);
            Surfaces.Card(g, new RectangleF(656 * k, 1290 * k, 600 * k, 120 * k), null, dpi);
            Surfaces.EmptyState(g, new RectangleF(656 * k, 1290 * k, 600 * k, 120 * k), IconName.Check, 32, "No problems found", "12,507 files checked. Last scan today 03:12.", dpi, top: 1304 * k);
        });

        return ScreenRenderer.Capture(c, (int)Math.Round(1280 * s), (int)Math.Round(1434 * s));
    }

    private static void TwoLine(Graphics g, float k, int dpi, float y, string title, string detail, string size, string path, bool hover, bool selected)
    {
        RectangleF r = new(24 * k, y * k, 1232 * k, 44 * k);
        RowPaint.Background(g, r, hover, selected, 32 * k, dpi);
        Color behind = selected ? T.Selected : hover ? T.Hover : T.Surface;
        Draw.Severity(g, Level.High, 40 * k, (y + 14) * k, 16, dpi, T.HighText, behind);
        Draw.Text(g, title, F(FontRole.BodyStrong, dpi), 68 * k, (y + 13) * k, T.Text);
        Draw.Text(g, detail, F(FontRole.Body, dpi), 68 * k, (y + 31) * k, T.Text2);
        Font body = F(FontRole.Body, dpi);
        if (hover)
        {
            Rectangle details = new((int)Math.Round(1144 * k), (int)Math.Round((y + 8) * k), (int)Math.Round(64 * k), (int)Math.Round(28 * k));
            KitButton.Render(g, details, ButtonVariant.Secondary, "Details", null, false, default, dpi);
            Rectangle more = new((int)Math.Round(1212 * k), (int)Math.Round((y + 8) * k), (int)Math.Round(28 * k), (int)Math.Round(28 * k));
            KitButton.Render(g, more, ButtonVariant.Subtle, "", IconName.More, false, default, dpi);
            Draw.TextRight(g, path, body, 1024 * k, (y + 22) * k, T.Text3);
        }
        else
        {
            Draw.TextRight(g, size, body, 1240 * k, (y + 22) * k, T.Text2);
            Draw.TextRight(g, path, body, 1156 * k, (y + 22) * k, T.Text3);
        }

        RowPaint.Divider(g, 68 * k, 1256 * k, (y + 44) * k, dpi);
    }

    private static void AddButton(SheetCanvas c, string text, ButtonVariant variant, IconName? icon, float x, float y, bool compact, PreviewState state, int dpi)
    {
        KitButton button = new() { Text = text, Variant = variant, Icon = icon, Compact = compact, Preview = state };
        float width = KitButton.MeasureWidth(text, icon is not null, false, variant, compact, dpi) / (dpi / 96f);
        c.Add(button, x, y, width, compact ? 28 : 32);
    }

    private static void AddChip(SheetCanvas c, string text, string? count, Level? level, IconName? icon, bool on, PreviewState state, float x, int dpi)
    {
        Chip chip = new() { Text = text, Count = count, Severity = level, Icon = icon, On = on, Preview = state };
        float width = Chip.Measure(text, count, level is not null || icon is not null, on, dpi) / (dpi / 96f);
        c.Add(chip, x, 502, width, 28);
    }

    // ---------------------------------------------------------------------------------------------
    // Dialogs, menus and transient surfaces (1280 × 1396)
    // ---------------------------------------------------------------------------------------------

    public static Bitmap Dialogs(int dpi)
    {
        SheetCanvas c = new(dpi);
        float s = dpi / 96f;
        void Label(string text, float x, float y) => c.Draw((g, k) => Draw.Text(g, text, F(FontRole.CaptionStrong, dpi), x * k, y * k, T.Text3));
        Rectangle R(float x, float y, float w, float h, float k) => new((int)Math.Round(x * k), (int)Math.Round(y * k), (int)Math.Round(w * k), (int)Math.Round(h * k));

        c.Draw((g, k) => Draw.Text(g, "Dialogs, menus and transient surfaces", F(FontRole.Title, dpi), 24 * k, 28 * k, T.Text));

        // A: confirm a repair
        Label("A  Confirm a repair", 24, 60);
        c.Draw((g, k) =>
        {
            Surfaces.DialogSurface(g, R(24, 76, 480, 400, k), "Rebuild the index of 38 videos?", dpi);
            Font body = F(FontRole.Body, dpi);
            Draw.Text(g, "Each video is rewritten into a new file with a working index. The picture and", body, 48 * k, 146 * k, T.Text);
            Draw.Text(g, "sound are copied bit for bit and checked before anything is replaced.", body, 48 * k, 166 * k, T.Text);
            (IconName icon, string text)[] facts = [(IconName.Video, "38 videos, 61.4 GB in total"), (IconName.Clock, "About 4 minutes"), (IconName.Quarantine, "Originals go to quarantine for 30 days"), (IconName.Undo, "You can undo this")];
            for (int i = 0; i < facts.Length; i++)
            {
                Draw.Icon(g, facts[i].icon, 48 * k, (192 + 28 * i) * k, 16, dpi, T.Text2);
                Draw.Text(g, facts[i].text, body, 76 * k, (200 + 28 * i) * k, T.Text);
            }

            Surfaces.InfoBar(g, new RectangleF(48 * k, 308 * k, 432 * k, 40 * k), Level.Medium, "2 changed videos will be skipped.", "Show", dpi);
            KitCheckBox.Render(g, R(48, 364, 18, 18, k), true, false, false, false, dpi);
            Draw.Text(g, "Pause the running scan while this happens", body, 76 * k, 373 * k, T.Text);
        });
        AddDialogButton(c, "Cancel", ButtonVariant.Secondary, PreviewState.None, 303, 420, dpi);
        AddDialogButton(c, "Rebuild index", ButtonVariant.Primary, PreviewState.Focus, 375, 420, dpi);

        // B: confirm a permanent deletion
        Label("B  Confirm a permanent deletion", 536, 60);
        c.Draw((g, k) =>
        {
            Surfaces.DialogSurface(g, R(536, 76, 400, 204, k), "Delete 41 files for good?", dpi);
            Font body = F(FontRole.Body, dpi);
            Draw.Text(g, "These are the originals kept in quarantine, 12.8 GB. This", body, 560 * k, 146 * k, T.Text);
            Draw.Text(g, "cannot be undone.", body, 560 * k, 166 * k, T.Text);
        });
        AddDialogButton(c, "Keep them", ButtonVariant.Secondary, PreviewState.Focus, 703, 224, dpi);
        AddDialogButton(c, "Delete for good", ButtonVariant.Danger, PreviewState.Hover, 797, 224, dpi);

        // C: context menu
        Label("C  Context menu", 968, 60);
        c.Draw((g, k) =>
        {
            MenuEntry[] entries =
            [
                new("Details", IconName.Info, "Enter"), new("Export replacement list", IconName.Report, "Ctrl+Enter"), new("Scan again", IconName.Refresh, "Shift+F5"),
                MenuEntry.Separator,
                new("Ignore", IconName.Ignore, "Ctrl+I"), new("Ignore this rule for this folder", IconName.Ignore, "None"),
                MenuEntry.Separator,
                new("Show in folder", IconName.Folder, "Alt+Enter"), new("Copy path", IconName.Copy, "Ctrl+C"),
                MenuEntry.Separator,
                new("Move to quarantine", IconName.Quarantine, "Del")
            ];
            Size size = KitMenu.Measure(entries, dpi);
            Rectangle menu = new((int)Math.Round(968 * k), (int)Math.Round(76 * k), size.Width, size.Height);
            Popup(g, menu, 8, dpi);
            KitMenu.PaintMenu(g, menu, entries, 1, dpi);
        });

        // D: toast
        Label("D  Toast", 536, 316);
        c.Draw((g, k) =>
        {
            Rectangle toast = R(536, 332, 360, 64, k);
            Popup(g, toast, 8, dpi);
            ToastPopup.PaintToast(g, toast, Level.Ok, "Index rebuilt for 36 videos", "2 skipped  ·  originals are in quarantine", "Undo", -1, dpi);
        });

        // E: tooltip
        Label("E  Tooltip", 536, 424);
        c.Draw((g, k) =>
        {
            const string tip = @"D:\Music\Night Drive\Overpass\Night Drive - 07 - Overpass.flac";
            Size size = KitTooltip.Measure(tip, dpi);
            Rectangle box = new((int)Math.Round(536 * k), (int)Math.Round(440 * k), size.Width, size.Height);
            Popup(g, box, 4, dpi);
            KitTooltip.PaintTooltip(g, box, tip, dpi);
        });

        // F: before a large scan
        Label("F  Before a large scan", 24, 516);
        c.Draw((g, k) =>
        {
            Surfaces.DialogSurface(g, R(24, 532, 480, 420, k), "Scan 58,210 files?", dpi);
            Draw.Text(g, "Music, Photos and Videos  ·  3.5 TB", F(FontRole.Body, dpi), 48 * k, 598 * k, T.Text2);
            Option(g, k, dpi, R(48, 620, 432, 56, k), "Quick", "Checks structure and fingerprints new files", "About 25 min", false);
            Option(g, k, dpi, R(48, 684, 432, 56, k), "Standard", "Decodes audio and photos fully, samples video", "About 3 h 20 min", true);
            Option(g, k, dpi, R(48, 748, 432, 56, k), "Deep", "Also decodes every video frame", "About 19 h", false);
            Font caption = F(FontRole.Caption, dpi);
            Draw.Text(g, "Estimates come from earlier scans on these drives. You can pause or stop at any time,", caption, 48 * k, 824 * k, T.Text3);
            Draw.Text(g, "and a stopped scan continues where it left off.", caption, 48 * k, 840 * k, T.Text3);
        });
        AddDialogButton(c, "Cancel", ButtonVariant.Secondary, PreviewState.None, 322, 896, dpi);
        AddDialogButton(c, "Start scan", ButtonVariant.Primary, PreviewState.Focus, 394, 896, dpi);

        // G: add or edit a folder
        Label("G  Add or edit a folder", 536, 516);
        c.Draw((g, k) =>
        {
            Surfaces.DialogSurface(g, R(536, 532, 560, 492, k), "Add folder", dpi);
            Draw.Icon(g, IconName.Close, 1056 * k, 558 * k, 16, dpi, T.Text);
            Font body = F(FontRole.Body, dpi);
            Draw.Text(g, "Folder", body, 560 * k, 608 * k, T.Text2);
            KitTextBox.RenderBox(g, R(664, 592, 323, 32, k), @"D:\Music", "", false, false, false, true, false, dpi);
            KitButton.Render(g, R(995, 592, 77, 32, k), ButtonVariant.Secondary, "Browse…", null, false, default, dpi);
            Draw.Text(g, "Name", body, 560 * k, 652 * k, T.Text2);
            KitTextBox.RenderBox(g, R(664, 636, 408, 32, k), "Music", "", false, false, false, true, false, dpi);
            Draw.Text(g, "Use as", body, 560 * k, 704 * k, T.Text2);
            Option(g, k, dpi, R(664, 684, 408, 52, k), "Library", "Scan everything here and report problems", null, true, 23);
            Option(g, k, dpi, R(664, 744, 408, 52, k), "Backup", "Only record fingerprints, so damaged files can be restored from here", null, false, 23);
            Draw.Text(g, "Contains", body, 560 * k, 818 * k, T.Text2);
            float x = 664;
            foreach (string name in new[] { "Photos", "Videos", "Music" })
            {
                KitCheckBox.Render(g, R(x, 809, 18, 18, k), true, false, false, false, dpi);
                Draw.Text(g, name, body, (x + 28) * k, 818 * k, T.Text);
                x += 28 + Draw.TextWidth(name, body) / k + 24;
            }

            Draw.Text(g, "Skip", body, 560 * k, 860 * k, T.Text2);
            KitTextBox.RenderBox(g, R(664, 844, 408, 32, k), @"*\Samples\*; *.part", "", false, false, false, true, false, dpi);
            Draw.Text(g, "Folder or file patterns, separated by semicolons", F(FontRole.Caption, dpi), 664 * k, 888 * k, T.Text3);
            Surfaces.InfoBar(g, new RectangleF(560 * k, 908 * k, 512 * k, 36 * k), Level.Low, "3,201 media files found  ·  96 GB", null, dpi);
        });
        AddDialogButton(c, "Cancel", ButtonVariant.Secondary, PreviewState.None, 915, 968, dpi);
        AddDialogButton(c, "Add folder", ButtonVariant.Primary, PreviewState.Focus, 987, 968, dpi);

        // H: notifications flyout
        Label("H  Notifications", 24, 976);
        c.Draw((g, k) =>
        {
            Rectangle panel = R(24, 992, 380, 372, k);
            Popup(g, panel, 8, dpi);
            Font body = F(FontRole.Body, dpi);
            Draw.Text(g, "Notifications", F(FontRole.Subtitle, dpi), 40 * k, 1020 * k, T.Text);
            Draw.TextRight(g, "Mark all read", body, 388 * k, 1020 * k, T.Accent);
            Draw.Line(g, T.Stroke, k, 24 * k, 1047.5f * k, 404 * k, 1047.5f * k);
            (Level level, string title, string detail, string time, bool unread, bool hover)[] items =
            [
                (Level.Critical, "3 new problems found", "1 critical, 2 high in Music. Scan finished 03:12.", "03:12", true, false),
                (Level.Ok, "Index rebuilt for 36 videos", "2 skipped. Originals are in quarantine.", "Yesterday", true, true),
                (Level.Low, "Update available", "Version 0.2.0.4 is ready to install.", "Mon", false, false),
                (Level.Medium, "Folder not connected", "Backup (E:) was not found. Its files are untouched.", "12 Nov", false, false)
            ];
            for (int i = 0; i < items.Length; i++)
            {
                float top = 1048 + 68 * i;
                if (items[i].hover)
                {
                    g.FillRectangle(ThemeService.Resources.Brush(T.Hover), 25 * k, top * k, 378 * k, 68 * k);
                    Draw.FillRounded(g, new RectangleF(25 * k, (top + 8) * k, 3 * k, 52 * k), 1.5f * k, T.StrokeStrong);
                }

                if (items[i].unread)
                {
                    Draw.Circle(g, T.Accent, 34 * k, (top + 24) * k, 3 * k);
                }

                Color behind = items[i].hover ? T.Hover : T.Raised;
                Draw.Severity(g, items[i].level, 46 * k, (top + 16) * k, 16, dpi, T.LevelColors(items[i].level).Text, behind);
                Draw.Text(g, items[i].title, F(FontRole.BodyStrong, dpi), 74 * k, (top + 24) * k, T.Text);
                Draw.TextRight(g, items[i].time, F(FontRole.Caption, dpi), 388 * k, (top + 24) * k, T.Text3);
                Draw.Text(g, items[i].detail, F(FontRole.Caption, dpi), 74 * k, (top + 45) * k, T.Text2);
                Draw.Line(g, T.Stroke, k, 40 * k, (top + 67.5f) * k, 388 * k, (top + 67.5f) * k);
            }

            Draw.Text(g, "Clear all", body, 333.1f * k, 1342 * k, T.Text);
        });

        // I: drop-down list
        Label("I  Drop-down list", 536, 1112);
        c.Draw((g, k) =>
        {
            DropDown.Render(g, R(536, 1128, 180, 32, k), "Up to High", false, true, false, false, false, false, dpi);
            string[] items = ["Up to Critical", "Up to High", "Up to Medium", "Up to Low"];
            Rectangle list = R(536, 1164, 180, 136, k);
            Popup(g, list, 8, dpi);
            DropDownList.PaintList(g, list, items, 1, 2, dpi);
        });

        return ScreenRenderer.Capture(c, (int)Math.Round(1280 * s), (int)Math.Round(1396 * s));
    }

    /// <summary>A floating surface painted inline: shadow, Raised fill, Stroke border.</summary>
    private static void Popup(Graphics g, Rectangle r, int radius, int dpi)
    {
        float s = dpi / 96f;
        Draw.Shadow(g, r, (int)Math.Round(radius * s), dpi);
        Draw.PrepareShapes(g);
        Draw.FillRounded(g, r, radius * s, T.Raised);
        Draw.StrokeRounded(g, r, radius * s, T.Stroke, s);
    }

    private static void Option(Graphics g, float k, int dpi, Rectangle r, string title, string detail, string? estimate, bool selected, float radioOffset = 23)
    {
        Draw.FillRounded(g, r, 4 * k, selected ? T.Selected : T.Bg);
        Draw.StrokeRounded(g, r, 4 * k, selected ? T.Accent : T.Stroke, k);
        float cy = r.Y + r.Height / 2f;
        Radio.Render(g, r.X + radioOffset * k, cy, selected, false, dpi);
        Draw.Text(g, title, F(FontRole.BodyStrong, dpi), r.X + 46 * k, cy - 9 * k, T.Text);
        Draw.Text(g, detail, F(FontRole.Caption, dpi), r.X + 46 * k, cy + 10 * k, T.Text2);
        if (estimate is not null)
        {
            Draw.TextRight(g, estimate, F(FontRole.Body, dpi), r.Right - 14 * k, cy, T.Text);
        }
    }

    private static void AddDialogButton(SheetCanvas c, string text, ButtonVariant variant, PreviewState state, float x, float y, int dpi)
    {
        KitButton button = new() { Text = text, Variant = variant, Preview = state, Backdrop = static t => t.Raised };
        float width = KitButton.MeasureWidth(text, false, false, variant, false, dpi) / (dpi / 96f);
        c.Add(button, x, y, width, 32);
    }

    // ---------------------------------------------------------------------------------------------
    // Icons and app icon (1280 × 852)
    // ---------------------------------------------------------------------------------------------

    public static Bitmap Icons(int dpi)
    {
        SheetCanvas c = new(dpi);
        float s = dpi / 96f;
        c.Draw((g, k) =>
        {
            Draw.Text(g, "Icons", F(FontRole.Title, dpi), 24 * k, 28 * k, T.Text);
            Draw.Text(g, "16 × 16 grid, 1.5 px stroke, round caps and joins. Shown at 16 px, at 32 px, and on the accent colour.", F(FontRole.Body, dpi), 24 * k, 56 * k, T.Text2);
            IconName[] icons = Enum.GetValues<IconName>().Where(i => i != IconName.BellOff).ToArray();
            for (int i = 0; i < icons.Length; i++)
            {
                float x = 24 + i % 8 * 154;
                float y = 84 + i / 8 * 92;
                Surfaces.Card(g, new RectangleF(x * k, y * k, 142 * k, 80 * k), null, dpi);
                Draw.Icon(g, icons[i], (x + 20) * k, (y + 18) * k, 16, dpi, T.Text);
                Draw.Icon(g, icons[i], (x + 56) * k, (y + 10) * k, 32, dpi, T.Text);
                Draw.FillRounded(g, new RectangleF((x + 106) * k, (y + 14) * k, 24 * k, 24 * k), 4 * k, T.Accent);
                Draw.Icon(g, icons[i], (x + 110) * k, (y + 18) * k, 16, dpi, T.OnAccent);
                Draw.TextCentered(g, IconGeometry.Key(icons[i]), F(FontRole.Caption, dpi), (x + 71) * k, (y + 62) * k, T.Text2);
            }

            Draw.Text(g, "App icon", F(FontRole.Subtitle, dpi), 24 * k, 652 * k, T.Text);
            Draw.Text(g, "Standard version at 32 px and larger; small version at 24 px and smaller.", F(FontRole.Body, dpi), 24 * k, 678 * k, T.Text2);
            (float x, float y, float size)[] marks = [(24, 700, 128), (180, 732, 64), (272, 740, 48), (348, 748, 32), (408, 752, 24), (460, 756, 16)];
            foreach ((float x, float y, float size) in marks)
            {
                IconArt.Paint(g, x * k, y * k, size * k);
            }
        });
        return ScreenRenderer.Capture(c, (int)Math.Round(1280 * s), (int)Math.Round(852 * s));
    }

    // ---------------------------------------------------------------------------------------------
    // Disk Monitor gallery: what the sheets do not cover
    // ---------------------------------------------------------------------------------------------

    public static Bitmap Gallery(int dpi)
    {
        SheetCanvas c = new(dpi);
        float s = dpi / 96f;
        c.Draw((g, k) =>
        {
            Font caption = F(FontRole.CaptionStrong, dpi);
            Draw.Text(g, "Disk Monitor additions", F(FontRole.Title, dpi), 24 * k, 28 * k, T.Text);

            Draw.Text(g, "SEVERITY AND HEALTH BADGES", caption, 24 * k, 64 * k, T.Text3);
            float x = 24;
            foreach (Tier tier in TierExtensions.Descending)
            {
                x += Badges.Tier(g, x * k, 80 * k, tier, dpi) / k + 12;
            }

            foreach (Core.Drives.HealthStatus health in new[] { Core.Drives.HealthStatus.Good, Core.Drives.HealthStatus.Caution, Core.Drives.HealthStatus.Bad, Core.Drives.HealthStatus.Unknown })
            {
                x += Badges.Health(g, x * k, 80 * k, health, dpi) / k + 12;
            }

            Draw.Text(g, "STATE ICONS", caption, 24 * k, 128 * k, T.Text3);
            x = 24;
            foreach (StateIcon state in Enum.GetValues<StateIcon>())
            {
                Draw.State(g, state, x * k, 144 * k, 16, dpi, T.Text3);
                x += 32;
            }

            Draw.Text(g, "METRIC TILES", caption, 24 * k, 192 * k, T.Text3);
            Surfaces.Card(g, new RectangleF(24 * k, 208 * k, 600 * k, 84 * k), null, dpi);
            DataGraphics.MetricTile(g, new RectangleF(40 * k, 224 * k, 128 * k, 52 * k), "TEMPERATURE", "38 °C", null, dpi);
            DataGraphics.MetricTile(g, new RectangleF(176 * k, 224 * k, 128 * k, 52 * k), "POWER-ON TIME", "3.0 years", null, dpi);
            DataGraphics.MetricTile(g, new RectangleF(312 * k, 224 * k, 128 * k, 52 * k), "REALLOCATED", "8", Tier.Medium, dpi);
            DataGraphics.MetricTile(g, new RectangleF(448 * k, 224 * k, 128 * k, 52 * k), "SPARE CAPACITY", "8%", Tier.Critical, dpi);

            Draw.Text(g, "USAGE BARS", caption, 24 * k, 320 * k, T.Text3);
            (double used, double free)[] usages = [(0.42, 58), (0.91, 9), (0.97, 3)];
            for (int i = 0; i < usages.Length; i++)
            {
                DataGraphics.UsageBar(g, new RectangleF(24 * k, (340 + 24 * i) * k, 200 * k, 8 * k), usages[i].used, usages[i].free, 5);
                DataGraphics.UsageBar(g, new RectangleF(240 * k, (342 + 24 * i) * k, 200 * k, 4 * k), usages[i].used, usages[i].free, 5);
            }

            Draw.Text(g, "SEVERITY BAR", caption, 24 * k, 432 * k, T.Text3);
            Dictionary<Tier, int> counts = new() { [Tier.Critical] = 3, [Tier.High] = 42, [Tier.Medium] = 9, [Tier.Low] = 2 };
            DataGraphics.SeverityBar(g, new RectangleF(24 * k, 448 * k, 320 * k, 8 * k), counts, dpi);

            Draw.Text(g, "DONUT AND TIMELINE", caption, 24 * k, 488 * k, T.Text3);
            DataGraphics.Donut(g, 64 * k, 544 * k, 40 * k, 10 * k, 0.75, T.OkFill);
            Draw.Severity(g, Level.Ok, 54 * k, 534 * k, 20, dpi, T.OkText, T.Bg);
            string[] lines = ["Bad block on disk", "I/O error during paging operation", "File system corruption detected"];
            for (int i = 0; i < lines.Length; i++)
            {
                DataGraphics.TimelineEntry(g, 140 * k, (520 + 28 * i) * k, 156 * k, 460 * k, lines[i], i == 0 ? "Today 12:20" : "Sun 09:14", i == 0, i == lines.Length - 1, 28 * k, dpi);
            }

            Draw.Text(g, "TRAY BADGES ON A DARK AND A LIGHT TASKBAR", caption, 24 * k, 640 * k, T.Text3);
            Color[] taskbars = [Color.FromArgb(0x20, 0x20, 0x20), Color.FromArgb(0xEE, 0xEE, 0xEE)];
            for (int row = 0; row < 2; row++)
            {
                g.FillRectangle(ThemeService.Resources.Brush(taskbars[row]), 24 * k, (656 + row * 48) * k, 360 * k, 40 * k);
                int i = 0;
                foreach (TrayBadge badge in Enum.GetValues<TrayBadge>())
                {
                    int size = (int)Math.Round(16 * k);
                    using Bitmap icon = IconArt.Render(size);
                    using (Graphics ig = Graphics.FromImage(icon))
                    {
                        IconArt.PaintBadge(ig, size, badge);
                    }

                    using Bitmap big = new(size * 2, size * 2);
                    using (Graphics bg = Graphics.FromImage(big))
                    {
                        bg.InterpolationMode = InterpolationMode.NearestNeighbor;
                        bg.PixelOffsetMode = PixelOffsetMode.Half;
                        bg.DrawImage(icon, 0, 0, size * 2, size * 2);
                    }

                    g.DrawImageUnscaled(big, (int)((40 + i * 56) * k), (int)((660 + row * 48) * k));
                    i++;
                }
            }
        });
        return ScreenRenderer.Capture(c, (int)Math.Round(1280 * s), (int)Math.Round(900 * s));
    }
}
