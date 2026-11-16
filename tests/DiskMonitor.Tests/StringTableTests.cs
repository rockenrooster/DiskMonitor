using System.Text;
using System.Text.RegularExpressions;

namespace DiskMonitor.Tests;

/// <summary>
/// Every user-facing string lives in <c>App/Strings.cs</c> (or, for rule texts, the rule catalog).
/// This test scans the source for string literals that look like words for people and fails on any
/// found elsewhere, apart from the exemptions below, each with its reason (CORE-13, UI-13).
/// </summary>
public sealed partial class StringTableTests
{
    /// <summary>Files whose literals are not checked, and why.</summary>
    private static readonly Dictionary<string, string> ExemptFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["App/Strings.cs"] = "the string table itself",
        ["Core/Events/EventRules.cs"] = "rule titles and texts are catalog content, and its matchers read Windows' English text",
        ["Diagnostics/SelfCheck.cs"] = "the self-check prints a diagnostic report, not UI",
        ["Diagnostics/DemoData.cs"] = "invented demo data (fixtures)",
        ["Diagnostics/ReferenceSheets.cs"] = "sample labels copied from MediaVerifier's reference sheets (fixtures)",
        ["UI/Theme/Theme.cs"] = "the pre-spec theme, replaced by ThemeService in UI-01 (milestone M2)"
    };

    /// <summary>Exact literals that look like words but are identifiers, values or protocol text.</summary>
    private static readonly Dictionary<string, string> AllowedLiterals = new(StringComparer.Ordinal)
    {
        // Settings values, page keys, log names and other identifiers that are stored or compared, not shown.
        ["System"] = "event log name and theme setting value",
        ["Application"] = "event log name",
        ["Light"] = "theme setting value",
        ["Dark"] = "theme setting value",
        ["Off"] = "routine scan setting value",
        ["Weekly"] = "routine scan setting value",
        ["Monthly"] = "routine scan setting value",
        ["Tier"] = "events grouping setting value",
        ["Drive"] = "events grouping setting value",
        ["None"] = "events grouping setting value",
        ["Ignore"] = "rule override value",
        ["Overview"] = "page key",
        ["Events"] = "page key",
        ["Drives"] = "page key",
        ["CheckDisk"] = "page key",
        ["Settings"] = "page key",
        ["Test"] = "log name of injected test events",
        ["Disk Monitor"] = "log name of health-check events (DriveMonitor.HealthLog)",
        ["Health check"] = "provider name of health-check events (DriveMonitor.HealthProvider)",
        ["DiskMonitor"] = "registry value, pipe and file names",
        ["DiskMonitor/"] = "HTTP user agent prefix",
        ["DISKMONITOR_DATA_DIR"] = "environment variable name",
        ["ProgramW6432"] = "environment variable name",
        ["ExistingPageFiles"] = "registry value name",
        ["Group"] = "registry value name",
        ["SCSI Miniport"] = "registry load-group name",
        ["DisplayName"] = "registry value name",
        ["IconUri"] = "registry value name",
        ["URL Protocol"] = "registry value name",
        ["URL:"] = "registry value prefix for a URL protocol",
        ["ATA"] = "placeholder vendor string reported by drives",
        ["NVMe"] = "placeholder vendor string reported by drives",
        ["NTFS"] = "file system name compared with Windows' value",
        ["ReFS"] = "file system name compared with Windows' value",
        ["A:"] = "floppy drive letter skipped during scans",
        ["B:"] = "floppy drive letter skipped during scans",
        ["JOB"] = "helper pipe protocol word",
        ["CANCEL"] = "helper pipe protocol word",
        ["D"] = ".NET long date format specifier",
        ["T"] = ".NET long time format specifier",
        ["N0"] = ".NET number format specifier",
        ["Progress:"] = "matches CHKDSK's English progress line",
        ["next time the system restarts"] = "matches CHKDSK's English output",
        ["will be checked"] = "matches CHKDSK's English output",
        ["chkdsk "] = "the command line shown for a run",
        ["Segoe UI"] = "font family",
        ["Segoe UI Semibold"] = "font family",
        ["Consolas"] = "font family",
        ["DarkMode_Explorer"] = "native theme name",
        ["Explorer"] = "native theme name",
        ["(Level=1 or Level=2 or Level=3) or (Provider[@Name='Microsoft-Windows-Ntfs'] and EventID=98)"] = "event log XPath query",
        ["Provider[@Name='Chkdsk' or @Name='Microsoft-Windows-Chkdsk' or @Name='Wininit' or @Name='Microsoft-Windows-Wininit']"] = "event log XPath query",
        [" and "] = "event log XPath query fragment",
        ["TimeCreated[timediff(@SystemTime) <= "] = "event log XPath query fragment",
        ["manifest signature is invalid"] = "update manifest detail; logged, never shown",
        ["manifest is empty"] = "update manifest detail; logged, never shown",
        ["manifest schema version is unsupported"] = "update manifest detail; logged, never shown",
        ["manifest tag does not match release tag"] = "update manifest detail; logged, never shown",
        ["manifest version does not match release version"] = "update manifest detail; logged, never shown",
        ["manifest asset name is invalid"] = "update manifest detail; logged, never shown",
        ["manifest size does not match release asset"] = "update manifest detail; logged, never shown",
        ["manifest createdUtc is invalid"] = "update manifest detail; logged, never shown",
        ["manifest download URL is invalid"] = "update manifest detail; logged, never shown",
        ["manifest SHA-256 is invalid"] = "update manifest detail; logged, never shown",
        ["Repository root not found."] = "test support message",
        ["Updates"] = "folder name for downloaded installers",
        ["HarddiskVolumeShadowCopy"] = "kernel device name matched in event messages",
        ["Y\r\n"] = "answer typed into CHKDSK's prompt",
        ["N\r\nY\r\n"] = "answers typed into CHKDSK's prompts",
        [") and "] = "event log XPath query fragment",
        ["AppsUseLightTheme"] = "registry value name",
        ["Settings save"] = "log context passed through JsonFile.TryWrite",
        ["State save"] = "log context passed through JsonFile.TryWrite"
    };

    [Fact]
    public void NoUserFacingLiteralOutsideTheStringTable()
    {
        string root = Path.Combine(TestSupport.RepositoryRoot(), "src", "DiskMonitor");
        List<string> failures = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)
                || ExemptFiles.ContainsKey(relative))
            {
                continue;
            }

            string source = File.ReadAllText(file);
            foreach (Literal literal in LiteralScanner.Scan(source))
            {
                if (LooksUserFacing(literal.Text) && !IsExempt(source, literal))
                {
                    int line = source.AsSpan(0, literal.Start).Count('\n') + 1;
                    failures.Add($"{relative}:{line}: \"{literal.Text}\"");
                }
            }
        }

        Assert.True(failures.Count == 0, "User-facing literals outside Strings.cs:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ScannerFindsEveryKindOfLiteral()
    {
        const string source = """"
            var a = "Plain text";
            var b = @"Verbatim ""quoted"" text";
            var c = $"Hole {x} and {(y ? "Inner text" : "z")} end";
            var d = $@"Verbatim {v} hole";
            char e = '"';
            // "Commented out"
            /* "Block comment" */
            var f = $$"""
                Raw {{hole}} text
                """;
            """";
        List<string> texts = LiteralScanner.Scan(source).Select(l => l.Text).ToList();
        Assert.Contains("Plain text", texts);
        Assert.Contains("Verbatim \"quoted\" text", texts);
        Assert.Contains("Hole ", texts);
        Assert.Contains("Inner text", texts);
        Assert.Contains(" end", texts);
        Assert.Contains("Verbatim ", texts);
        Assert.Contains(" hole", texts);
        Assert.DoesNotContain("Commented out", texts);
        Assert.DoesNotContain("Block comment", texts);
        Assert.Contains(texts, t => t.Contains("Raw ", StringComparison.Ordinal));
    }

    /// <summary>A literal is checked when it has a letter and either a space or a leading capital.</summary>
    private static bool LooksUserFacing(string text) =>
        text.Any(char.IsLetter) && (text.Contains(' ') || char.IsUpper(text.TrimStart()[0]));

    private static bool IsExempt(string source, Literal literal)
    {
        string text = literal.Text;
        string trimmed = text.Trim();
        if (AllowedLiterals.ContainsKey(text) || AllowedLiterals.ContainsKey(trimmed))
        {
            return true;
        }

        // The first argument of ErrorLog.Write is a log context, written for the developer.
        string before = source[..literal.TokenStart].TrimEnd();
        if (before.EndsWith("ErrorLog.Write(", StringComparison.Ordinal))
        {
            return true;
        }

        // Attribute arguments: regex patterns, interop entry points, JSON property names.
        int lineStart = source.LastIndexOf('\n', Math.Max(0, literal.TokenStart - 1)) + 1;
        if (source[lineStart..literal.TokenStart].TrimStart().StartsWith('['))
        {
            return true;
        }

        return trimmed.Length == 0
            || trimmed.Contains('\\')                           // paths, registry keys, device names, escapes
            || trimmed.StartsWith('/') || trimmed.StartsWith('-') || trimmed.StartsWith('"') // command-line arguments
            || trimmed.Contains('<') || trimmed.Contains("/>", StringComparison.Ordinal) || trimmed.Contains("=\"", StringComparison.Ordinal) // XML markup
            || trimmed.Contains('|')                            // helper pipe protocol and key separators
            || Guid.TryParse(trimmed, out _)                    // COM and WinRT interface IDs
            || trimmed.StartsWith("http", StringComparison.Ordinal)                          // URLs
            || DottedIdentifier().IsMatch(trimmed)              // WinRT class names, AppUserModelID, pipe names
            || DriveLetter().IsMatch(trimmed);                  // "C:"
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_\-]*(\.[A-Za-z0-9_\-]+)+\.?$")]
    private static partial Regex DottedIdentifier();

    [GeneratedRegex(@"^[A-Z]:$")]
    private static partial Regex DriveLetter();

    internal readonly record struct Literal(int Start, int TokenStart, string Text);

    /// <summary>A small C# lexer that yields the text of string literals, and the text parts of interpolated ones.</summary>
    internal static class LiteralScanner
    {
        public static List<Literal> Scan(string s)
        {
            List<Literal> found = [];
            int i = 0;
            ScanCode(s, ref i, found, stopAtBrace: false);
            return found;
        }

        // Scans code until the end, or (inside an interpolation hole) until the closing brace.
        private static void ScanCode(string s, ref int i, List<Literal> found, bool stopAtBrace)
        {
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + 2;
                    continue;
                }

                if (c == '\'')
                {
                    i++;
                    while (i < s.Length && s[i] != '\'')
                    {
                        i += s[i] == '\\' ? 2 : 1;
                    }

                    i++;
                    continue;
                }

                if (c is '"' or '$' or '@')
                {
                    int prefixStart = i;
                    int dollars = 0;
                    bool verbatim = false;
                    int j = i;
                    while (j < s.Length && (s[j] == '$' || s[j] == '@'))
                    {
                        if (s[j] == '$')
                        {
                            dollars++;
                        }
                        else
                        {
                            verbatim = true;
                        }

                        j++;
                    }

                    if (j < s.Length && s[j] == '"' && (j == i || IsLiteralPrefixStart(s, prefixStart)))
                    {
                        i = j;
                        ReadString(s, ref i, found, dollars, verbatim, prefixStart);
                        continue;
                    }

                    i = Math.Max(j, i + 1);
                    continue;
                }

                if (stopAtBrace)
                {
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        if (depth == 0)
                        {
                            return;
                        }

                        depth--;
                    }
                }

                i++;
            }
        }

        private static bool IsLiteralPrefixStart(string s, int index) => index == 0 || !(char.IsLetterOrDigit(s[index - 1]) || s[index - 1] == '_');

        private static void ReadString(string s, ref int i, List<Literal> found, int dollars, bool verbatim, int tokenStart)
        {
            // Raw string: three or more quotes.
            int quotes = 0;
            while (i + quotes < s.Length && s[i + quotes] == '"')
            {
                quotes++;
            }

            if (quotes >= 3)
            {
                ReadRaw(s, ref i, found, quotes, dollars, tokenStart);
                return;
            }

            i++; // opening quote
            StringBuilder text = new();
            int segmentStart = i;
            while (i < s.Length)
            {
                char c = s[i];
                if (verbatim && c == '"' && i + 1 < s.Length && s[i + 1] == '"')
                {
                    text.Append('"');
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    i++;
                    break;
                }

                if (!verbatim && c == '\\' && i + 1 < s.Length)
                {
                    text.Append(Unescape(s[i + 1]));
                    i += 2;
                    continue;
                }

                if (dollars > 0 && c == '{')
                {
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        text.Append('{');
                        i += 2;
                        continue;
                    }

                    Flush(found, text, segmentStart, tokenStart);
                    i++;
                    ScanCode(s, ref i, found, stopAtBrace: true);
                    i++; // closing brace
                    segmentStart = i;
                    continue;
                }

                if (dollars > 0 && c == '}' && i + 1 < s.Length && s[i + 1] == '}')
                {
                    text.Append('}');
                    i += 2;
                    continue;
                }

                text.Append(c);
                i++;
            }

            Flush(found, text, segmentStart, tokenStart);
        }

        private static void ReadRaw(string s, ref int i, List<Literal> found, int quotes, int dollars, int tokenStart)
        {
            string fence = new('"', quotes);
            int start = i + quotes;
            int end = s.IndexOf(fence, start, StringComparison.Ordinal);
            if (end < 0)
            {
                end = s.Length;
            }

            string body = s[start..end];
            i = Math.Min(s.Length, end + quotes);
            if (dollars == 0)
            {
                found.Add(new Literal(start, tokenStart, body));
                return;
            }

            // Holes open with as many braces as there are dollar signs.
            string open = new('{', dollars);
            string close = new('}', dollars);
            int position = 0;
            while (position < body.Length)
            {
                int hole = body.IndexOf(open, position, StringComparison.Ordinal);
                if (hole < 0)
                {
                    found.Add(new Literal(start + position, tokenStart, body[position..]));
                    break;
                }

                if (hole > position)
                {
                    found.Add(new Literal(start + position, tokenStart, body[position..hole]));
                }

                int holeEnd = body.IndexOf(close, hole + dollars, StringComparison.Ordinal);
                position = holeEnd < 0 ? body.Length : holeEnd + dollars;
            }
        }

        private static void Flush(List<Literal> found, StringBuilder text, int start, int tokenStart)
        {
            if (text.Length > 0)
            {
                found.Add(new Literal(start, tokenStart, text.ToString()));
                text.Clear();
            }
        }

        private static char Unescape(char c) => c switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '0' => '\0',
            _ => c
        };
    }
}
