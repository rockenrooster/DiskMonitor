using System.Drawing.Drawing2D;
using System.Globalization;

namespace DiskMonitor.UI.Theming;

/// <summary>Every icon of design/icons.md. Names map to the kebab-case keys the design files use.</summary>
internal enum IconName
{
    Dashboard,
    Issues,
    Library,
    Scans,
    Schedule,
    Tools,
    Settings,
    Search,
    Bell,
    Filter,
    Refresh,
    Play,
    Pause,
    Stop,
    Close,
    Add,
    Check,
    ChevronRight,
    ChevronDown,
    ChevronLeft,
    ChevronUp,
    More,
    Folder,
    FolderAdd,
    Photo,
    Video,
    Music,
    Clock,
    Shield,
    ShieldCheck,
    Undo,
    Quarantine,
    Trash,
    Open,
    Copy,
    Download,
    Report,
    Info,
    Ignore,
    Drive,
    Update,
    Menu,
    Database,
    Engine,
    SortDown,
    SortUp,
    BellOff
}

/// <summary>
/// Icon geometry as data, on the 16 × 16 grid of design/icons.md. One primitive per entry, separated by
/// '|': <c>R x y w h r</c> rounded rectangle; <c>G points</c> polygon; <c>P points</c> polyline;
/// <c>L x1 y1 x2 y2</c> line; <c>C cx cy r</c> stroked circle; <c>F cx cy r</c> filled circle (also
/// stroked, as the SVG group's stroke applies to it); <c>S d</c> SVG path data (M, L, H, V, C, Z, absolute).
/// </summary>
internal static class IconGeometry
{
    private const string BellBody = "S M4 11.5 V7.5 C4 5 5.8 3 8 3 C10.2 3 12 5 12 7.5 V11.5|L 2.75 11.5 13.25 11.5|L 6.75 13.75 9.25 13.75";
    private const string FolderBody = "S M2 4.5 H6 L7.5 6 H14 V12.5 H2 Z";
    private const string ShieldBody = "S M8 2 L13 4 V8 C13 10.9 10.9 13.2 8 14 C5.1 13.2 3 10.9 3 8 V4 Z";

    public static readonly IReadOnlyDictionary<IconName, string> Source = new Dictionary<IconName, string>
    {
        [IconName.Dashboard] = "R 2.5 2.5 4.5 5 1|R 9 2.5 4.5 3 1|R 9 7.5 4.5 6 1|R 2.5 9.5 4.5 4 1",
        [IconName.Issues] = "G 8,2.5 14,13 2,13|L 8 6.5 8 9.5|F 8 11.25 0.5",
        [IconName.Library] = "L 3.5 2.5 3.5 13.5|L 7 2.5 7 13.5|L 10 3.5 13 13.5",
        [IconName.Scans] = "P 1.5,8 4.5,8 6.5,3.5 9.5,12.5 11.5,8 14.5,8",
        [IconName.Schedule] = "R 2.5 3.5 11 10 1.5|L 2.5 6.5 13.5 6.5|L 5.5 2 5.5 4.5|L 10.5 2 10.5 4.5|F 5.5 9.75 0.5|F 8 9.75 0.5|F 10.5 9.75 0.5",
        [IconName.Tools] = "R 2.5 5 11 8.5 1.5|P 6,5 6,3 10,3 10,5|L 8 7.5 8 11|L 6.25 9.25 9.75 9.25",
        [IconName.Settings] = "L 2.5 4.5 8 4.5|L 12 4.5 13.5 4.5|C 10 4.5 1.75|L 2.5 11.5 4 11.5|L 8 11.5 13.5 11.5|C 6 11.5 1.75",
        [IconName.Search] = "C 7 7 4.5|L 10.4 10.4 14 14",
        [IconName.Bell] = BellBody,
        [IconName.Filter] = "G 2.5,3.5 13.5,3.5 9.5,8.5 9.5,12.5 6.5,13.5 6.5,8.5",
        [IconName.Refresh] = "S M13 8 C13 10.76 10.76 13 8 13 C5.24 13 3 10.76 3 8 C3 5.24 5.24 3 8 3 C9.6 3 11 3.75 11.9 4.9|P 12.25,2.25 12.25,5.25 9.25,5.25",
        [IconName.Play] = "G 5,3.5 12.5,8 5,12.5",
        [IconName.Pause] = "L 5.5 3.5 5.5 12.5|L 10.5 3.5 10.5 12.5",
        [IconName.Stop] = "R 4 4 8 8 1",
        [IconName.Close] = "L 4 4 12 12|L 12 4 4 12",
        [IconName.Add] = "L 8 3 8 13|L 3 8 13 8",
        [IconName.Check] = "P 3,8.5 6.5,12 13,4.5",
        [IconName.ChevronRight] = "P 6,3.5 10.5,8 6,12.5",
        [IconName.ChevronDown] = "P 3.5,6 8,10.5 12.5,6",
        [IconName.ChevronLeft] = "P 10,3.5 5.5,8 10,12.5",
        [IconName.ChevronUp] = "P 3.5,10 8,5.5 12.5,10",
        [IconName.More] = "F 3.5 8 0.6|F 8 8 0.6|F 12.5 8 0.6",
        [IconName.Folder] = FolderBody,
        [IconName.FolderAdd] = FolderBody + "|L 8 7.75 8 10.75|L 6.5 9.25 9.5 9.25",
        [IconName.Photo] = "R 2.5 3.5 11 9 1.5|C 5.75 6.75 1|P 2.5,11 6,8 8.5,10.25 10.5,8.5 13.5,11",
        [IconName.Video] = "R 2 4 8.5 8 1.5|P 10.5,6.9 14,4.9 14,11.1 10.5,9.1",
        [IconName.Music] = "P 6,11.5 6,4 12.5,3 12.5,10.5|C 4.5 11.5 1.5|C 11 10.5 1.5",
        [IconName.Clock] = "C 8 8 5.5|P 8,4.75 8,8 10.25,9.5",
        [IconName.Shield] = ShieldBody,
        [IconName.ShieldCheck] = ShieldBody + "|P 5.75,8 7.4,9.6 10.25,6.5",
        [IconName.Undo] = "S M3.5 6.5 H10 C12 6.5 13.5 8 13.5 10 C13.5 12 12 13.5 10 13.5 H6.5|P 6,4 3.5,6.5 6,9",
        [IconName.Quarantine] = "R 2.5 3 11 3 0.75|P 3.5,6 3.5,12.5 12.5,12.5 12.5,6|L 6.5 8.75 9.5 8.75",
        [IconName.Trash] = "L 3 4.5 13 4.5|P 6,4.5 6,3 10,3 10,4.5|P 4.25,4.5 5,13.5 11,13.5 11.75,4.5|L 6.75 7 6.75 11|L 9.25 7 9.25 11",
        [IconName.Open] = "P 7,3.5 3.5,3.5 3.5,12.5 12.5,12.5 12.5,9|P 9.5,3.5 12.5,3.5 12.5,6.5|L 12.5 3.5 7.5 8.5",
        [IconName.Copy] = "R 5.5 5.5 8 8 1.5|P 10.5,3.5 10.5,2.5 2.5,2.5 2.5,10.5 3.5,10.5",
        [IconName.Download] = "L 8 2.5 8 10.5|P 4.5,7.5 8,11 11.5,7.5|L 3 13.5 13 13.5",
        [IconName.Report] = "S M4 2.5 H9.5 L12 5 V13.5 H4 Z|P 9.5,2.5 9.5,5 12,5|L 6 8 10 8|L 6 10.5 10 10.5",
        [IconName.Info] = "C 8 8 5.5|L 8 7.25 8 11|F 8 5.1 0.5",
        [IconName.Ignore] = "C 8 8 5.5|L 4.1 4.1 11.9 11.9",
        [IconName.Drive] = "R 2.5 4 11 8 1.5|L 2.5 9 13.5 9|F 11 10.5 0.4",
        [IconName.Update] = "C 8 8 5.5|P 5.5,8 8,5.5 10.5,8|L 8 5.5 8 11",
        [IconName.Menu] = "L 2.5 4.5 13.5 4.5|L 2.5 8 13.5 8|L 2.5 11.5 13.5 11.5",
        [IconName.Database] = "S M3 4.5 C3 3.4 5.2 2.5 8 2.5 C10.8 2.5 13 3.4 13 4.5 V11.5 C13 12.6 10.8 13.5 8 13.5 C5.2 13.5 3 12.6 3 11.5 Z|S M3 4.5 C3 5.6 5.2 6.5 8 6.5 C10.8 6.5 13 5.6 13 4.5|S M3 8 C3 9.1 5.2 10 8 10 C10.8 10 13 9.1 13 8",
        [IconName.Engine] = "R 4.5 4.5 7 7 1|L 6.5 2 6.5 4.5|L 9.5 2 9.5 4.5|L 6.5 11.5 6.5 14|L 9.5 11.5 9.5 14|L 2 6.5 4.5 6.5|L 2 9.5 4.5 9.5|L 11.5 6.5 14 6.5|L 11.5 9.5 14 9.5",
        [IconName.SortDown] = "P 4.5,6.5 8,10 11.5,6.5",
        [IconName.SortUp] = "P 4.5,9.5 8,6 11.5,9.5",
        [IconName.BellOff] = BellBody + "|L 2.5 2.5 13.5 13.5"
    };

    /// <summary>The kebab-case key the design files use, for example "chevron-down".</summary>
    public static string Key(IconName icon)
    {
        string name = icon.ToString();
        System.Text.StringBuilder key = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                key.Append('-');
            }

            key.Append(char.ToLowerInvariant(name[i]));
        }

        return key.ToString();
    }

    /// <summary>Builds the stroked outline and the filled dots of an icon, scaled from the 16 px grid.</summary>
    public static (GraphicsPath Stroke, GraphicsPath? Fill) Build(IconName icon, float scale) => Build(Source[icon], scale);

    public static (GraphicsPath Stroke, GraphicsPath? Fill) Build(string source, float scale)
    {
        GraphicsPath stroke = new();
        GraphicsPath? fill = null;
        foreach (string part in source.Split('|'))
        {
            string primitive = part.Trim();
            char kind = primitive[0];
            string body = primitive[1..].Trim();
            switch (kind)
            {
                case 'R':
                {
                    float[] v = Numbers(body);
                    stroke.StartFigure();
                    AddRoundedRect(stroke, v[0] * scale, v[1] * scale, v[2] * scale, v[3] * scale, v[4] * scale);
                    break;
                }

                case 'G':
                case 'P':
                {
                    PointF[] points = Points(body, scale);
                    stroke.StartFigure();
                    stroke.AddLines(points);
                    if (kind == 'G')
                    {
                        stroke.CloseFigure();
                    }

                    break;
                }

                case 'L':
                {
                    float[] v = Numbers(body);
                    stroke.StartFigure();
                    stroke.AddLine(v[0] * scale, v[1] * scale, v[2] * scale, v[3] * scale);
                    break;
                }

                case 'C':
                case 'F':
                {
                    float[] v = Numbers(body);
                    float r = v[2] * scale;
                    stroke.StartFigure();
                    stroke.AddEllipse(v[0] * scale - r, v[1] * scale - r, 2 * r, 2 * r);
                    if (kind == 'F')
                    {
                        fill ??= new GraphicsPath();
                        fill.AddEllipse(v[0] * scale - r, v[1] * scale - r, 2 * r, 2 * r);
                    }

                    break;
                }

                case 'S':
                    AddSvgPath(stroke, body, scale);
                    break;

                default:
                    throw new FormatException($"Unknown icon primitive '{kind}'.");
            }
        }

        return (stroke, fill);
    }

    /// <summary>Appends SVG path data (absolute M, L, H, V, C and Z only) to a path.</summary>
    public static void AddSvgPath(GraphicsPath path, string data, float scale, float offsetX = 0, float offsetY = 0)
    {
        string[] tokens = Tokenize(data);
        int i = 0;
        PointF current = default;
        PointF start = default;
        char command = 'M';
        path.StartFigure();
        while (i < tokens.Length)
        {
            if (char.IsLetter(tokens[i][0]))
            {
                command = tokens[i][0];
                i++;
                if (command == 'Z')
                {
                    path.CloseFigure();
                    current = start;
                    continue;
                }
            }

            switch (command)
            {
                case 'M':
                    current = new PointF(Parse(tokens[i]) * scale + offsetX, Parse(tokens[i + 1]) * scale + offsetY);
                    start = current;
                    path.StartFigure();
                    i += 2;
                    command = 'L'; // further pairs are implicit line-tos
                    break;
                case 'L':
                {
                    PointF next = new(Parse(tokens[i]) * scale + offsetX, Parse(tokens[i + 1]) * scale + offsetY);
                    path.AddLine(current, next);
                    current = next;
                    i += 2;
                    break;
                }

                case 'H':
                {
                    PointF next = new(Parse(tokens[i]) * scale + offsetX, current.Y);
                    path.AddLine(current, next);
                    current = next;
                    i += 1;
                    break;
                }

                case 'V':
                {
                    PointF next = new(current.X, Parse(tokens[i]) * scale + offsetY);
                    path.AddLine(current, next);
                    current = next;
                    i += 1;
                    break;
                }

                case 'C':
                {
                    PointF c1 = new(Parse(tokens[i]) * scale + offsetX, Parse(tokens[i + 1]) * scale + offsetY);
                    PointF c2 = new(Parse(tokens[i + 2]) * scale + offsetX, Parse(tokens[i + 3]) * scale + offsetY);
                    PointF end = new(Parse(tokens[i + 4]) * scale + offsetX, Parse(tokens[i + 5]) * scale + offsetY);
                    path.AddBezier(current, c1, c2, end);
                    current = end;
                    i += 6;
                    break;
                }

                default:
                    throw new FormatException($"Unsupported path command '{command}'.");
            }
        }
    }

    /// <summary>A rounded rectangle whose corners are quarter circles of radius <paramref name="r"/>.</summary>
    public static void AddRoundedRect(GraphicsPath path, float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        if (r <= 0.01f)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return;
        }

        float d = 2 * r;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
    }

    private static string[] Tokenize(string data)
    {
        List<string> tokens = [];
        int i = 0;
        while (i < data.Length)
        {
            char c = data[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            int start = i;
            i++;
            while (i < data.Length && (char.IsDigit(data[i]) || data[i] == '.'))
            {
                i++;
            }

            tokens.Add(data[start..i]);
        }

        return [.. tokens];
    }

    private static float Parse(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static float[] Numbers(string text) =>
        text.Split((char[])[' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray();

    private static PointF[] Points(string text, float scale) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(','))
            .Select(xy => new PointF(Parse(xy[0]) * scale, Parse(xy[1]) * scale))
            .ToArray();
}
