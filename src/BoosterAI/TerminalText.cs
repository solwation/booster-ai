using System.Globalization;
using System.Text;

namespace BoosterAI;

/// <summary>
/// Replays captured terminal output (the tail of a tab's <c>tabN.out</c>) on a small virtual terminal and
/// returns the text a reader would see: TUIs (claude, codex, agy) redraw their screen with cursor moves and
/// erases, so stripping escape sequences alone would show every intermediate frame. Handles cursor moves,
/// erase line/screen, insert/delete lines and characters, scroll regions, the alternate screen and
/// autowrap; colours, modes and OSC/DCS strings are skipped. The tail may start in the middle of a
/// redraw, so the result is the best reading of the last frame, not an exact copy.
/// </summary>
public sealed class TerminalText
{
    private const int ScrollbackLimit = 500;

    private readonly int _cols;
    private readonly int _rows;
    private readonly List<string> _scrollback = [];
    private string[][] _main;
    private string[][]? _alt;
    private int _row, _col, _top, _bottom;
    private int _savedRow, _savedCol;
    private bool _wrapPending;
    private bool _lineFeedReturns;

    private TerminalText(int rows, int cols)
    {
        _rows = Math.Clamp(rows, 2, 500);
        _cols = Math.Clamp(cols, 10, 1000);
        _main = NewScreen();
        _bottom = _rows - 1;
    }

    private string[][] Screen => _alt ?? _main;

    /// <summary>
    /// The last <paramref name="maxLines"/> lines of <paramref name="output"/> as shown on a terminal of
    /// <paramref name="rows"/> × <paramref name="cols"/>; trailing blanks and blank lines at either end are dropped,
    /// and runs of blank lines become one.
    /// </summary>
    public static IReadOnlyList<string> Render(string output, int rows, int cols, int maxLines)
    {
        // Text from a pipe (bs_stream's formatter) has bare line feeds, which a terminal's output processing
        // would turn into CR LF; a terminal capture carries its carriage returns itself.
        var t = new TerminalText(rows, cols) { _lineFeedReturns = !output.Contains('\r') };
        t.Feed(output);
        return t.Lines(maxLines);
    }

    /// <summary>
    /// Decodes a captured byte tail. When <paramref name="fromStart"/> is false the bytes begin at an arbitrary
    /// point, so everything up to the first line feed (a cut escape sequence or UTF-8 character) is dropped.
    /// The "Script started on …" header that util-linux script writes first is dropped as well.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes, bool fromStart)
    {
        if (!fromStart)
        {
            var nl = bytes.IndexOf((byte)'\n');
            bytes = nl < 0 ? [] : bytes[(nl + 1)..];
        }
        var text = Encoding.UTF8.GetString(bytes);
        if (fromStart && text.StartsWith("Script started on ", StringComparison.Ordinal))
        {
            var nl = text.IndexOf('\n');
            text = nl < 0 ? "" : text[(nl + 1)..];
        }
        // ... and the "Script done on …" footer it writes when the command exits.
        var done = text.LastIndexOf("\nScript done on ", StringComparison.Ordinal);
        if (done >= 0) text = text[..(done + 1)];
        return text;
    }

    private string[][] NewScreen() => Enumerable.Range(0, _rows).Select(_ => BlankLine()).ToArray();

    private string[] BlankLine() => Enumerable.Repeat(" ", _cols).ToArray();

    private void Feed(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\x1b') { i = Escape(s, i + 1); continue; }
            if (c == '\x9b') { i = Csi(s, i + 1); continue; }
            switch (c)
            {
                case '\r': _col = 0; _wrapPending = false; i++; continue;
                case '\n' or '\v' or '\f': if (_lineFeedReturns) _col = 0; LineFeed(); i++; continue;
                case '\b': if (_col > 0) _col--; _wrapPending = false; i++; continue;
                case '\t': _col = Math.Min(_cols - 1, (_col / 8 + 1) * 8); i++; continue;
            }
            if (c < ' ' || c == '\x7f' || c is >= '\x80' and < '\xa0') { i++; continue; }
            // One text element: a character with its surrogate pair or combining marks.
            if (Rune.DecodeFromUtf16(s.AsSpan(i), out var rune, out var len) != System.Buffers.OperationStatus.Done)
            {
                i++;
                continue;
            }
            var width = Width(rune);
            if (width == 0)
            {
                // A combining mark joins the cell written last.
                var cell = _wrapPending ? _col : _col - 1;
                if (cell >= 0) Screen[_row][cell] += s.Substring(i, len);
                i += len;
                continue;
            }
            Put(s.Substring(i, len), width);
            i += len;
        }
    }

    private void Put(string text, int width)
    {
        if (_wrapPending || _col + width > _cols)
        {
            _col = 0;
            LineFeed();
        }
        var line = Screen[_row];
        line[_col] = text;
        if (width == 2 && _col + 1 < _cols) line[_col + 1] = "";
        if (_col + width >= _cols) { _col = _cols - 1; _wrapPending = true; }
        else _col += width;
    }

    private void LineFeed()
    {
        _wrapPending = false;
        if (_row == _bottom) ScrollUp(1);
        else if (_row < _rows - 1) _row++;
    }

    private void ScrollUp(int n)
    {
        var screen = Screen;
        for (var k = 0; k < n; k++)
        {
            // Lines leaving the top of a full-screen region on the main screen go to the scrollback.
            if (_alt is null && _top == 0) AddScrollback(screen[_top]);
            for (var r = _top; r < _bottom; r++) screen[r] = screen[r + 1];
            screen[_bottom] = BlankLine();
        }
    }

    private void ScrollDown(int n)
    {
        var screen = Screen;
        for (var k = 0; k < n; k++)
        {
            for (var r = _bottom; r > _top; r--) screen[r] = screen[r - 1];
            screen[_top] = BlankLine();
        }
    }

    private void AddScrollback(string[] line)
    {
        _scrollback.Add(Text(line));
        if (_scrollback.Count > ScrollbackLimit) _scrollback.RemoveRange(0, _scrollback.Count - ScrollbackLimit);
    }

    private static string Text(string[] line) => string.Concat(line).TrimEnd();

    private int Escape(string s, int i)
    {
        if (i >= s.Length) return i;
        var c = s[i];
        switch (c)
        {
            case '[': return Csi(s, i + 1);
            case ']' or 'P' or '_' or '^' or 'X': return SkipString(s, i + 1);
            case '(' or ')' or '*' or '+' or '#' or '%' or ' ': return Math.Min(s.Length, i + 2);
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'D': LineFeed(); break;
            case 'E': _col = 0; LineFeed(); break;
            case 'M': ReverseIndex(); break;
            case 'c': Reset(); break;
        }
        return i + 1;
    }

    /// <summary>Skips an OSC/DCS/APC/PM/SOS string up to BEL or ST (ESC \).</summary>
    private static int SkipString(string s, int i)
    {
        for (; i < s.Length; i++)
        {
            if (s[i] == '\a' || s[i] == '\x9c') return i + 1;
            if (s[i] == '\x1b' && i + 1 < s.Length && s[i + 1] == '\\') return i + 2;
        }
        return i;
    }

    private int Csi(string s, int i)
    {
        var start = i;
        while (i < s.Length && s[i] is >= '0' and <= '?') i++; // parameters and private markers
        while (i < s.Length && s[i] is >= ' ' and <= '/') i++; // intermediates
        if (i >= s.Length) return i;
        var final = s[i];
        var raw = s[start..i];
        var isPrivate = raw.Length > 0 && raw[0] is '?' or '>' or '<' or '=';
        var hasIntermediate = raw.Length > 0 && raw[^1] is >= ' ' and <= '/';
        if (hasIntermediate) return i + 1; // e.g. DECSCUSR "CSI 2 q": not a cursor or screen change
        var p = Params(isPrivate ? raw[1..] : raw);
        int P(int k, int def) => k < p.Length && p[k] > 0 ? p[k] : def;
        // Anything but attributes and modes moves or edits at the cursor, which ends a pending autowrap.
        if (final is not ('m' or 'h' or 'l' or 'n' or 't' or 'c' or 'q' or 'p')) _wrapPending = false;
        if (isPrivate)
        {
            if (raw[0] == '?' && final is 'h' or 'l') PrivateMode(p, final == 'h');
            return i + 1;
        }
        switch (final)
        {
            case 'A': _row = Math.Max(_row >= _top ? _top : 0, _row - P(0, 1)); break;
            case 'B': _row = Math.Min(_row <= _bottom ? _bottom : _rows - 1, _row + P(0, 1)); break;
            case 'C' or 'a': _col = Math.Min(_cols - 1, _col + P(0, 1)); break;
            case 'D': _col = Math.Max(0, _col - P(0, 1)); break;
            case 'E': _row = Math.Min(_rows - 1, _row + P(0, 1)); _col = 0; break;
            case 'F': _row = Math.Max(0, _row - P(0, 1)); _col = 0; break;
            case 'G' or '`': _col = Math.Clamp(P(0, 1) - 1, 0, _cols - 1); break;
            case 'd': _row = Math.Clamp(P(0, 1) - 1, 0, _rows - 1); break;
            case 'H' or 'f':
                _row = Math.Clamp(P(0, 1) - 1, 0, _rows - 1);
                _col = Math.Clamp(P(1, 1) - 1, 0, _cols - 1);
                break;
            case 'J': EraseDisplay(p.Length > 0 ? p[0] : 0); break;
            case 'K': EraseLine(p.Length > 0 ? p[0] : 0); break;
            case 'X': Fill(_row, _col, Math.Min(_cols, _col + P(0, 1))); break;
            case 'P': DeleteChars(P(0, 1)); break;
            case '@': InsertChars(P(0, 1)); break;
            case 'L': if (_row >= _top && _row <= _bottom) WithTop(_row, () => ScrollDown(P(0, 1))); break;
            case 'M': if (_row >= _top && _row <= _bottom) WithTop(_row, () => ScrollUpNoScrollback(P(0, 1))); break;
            case 'S': ScrollUp(P(0, 1)); break;
            case 'T': ScrollDown(P(0, 1)); break;
            case 'r':
                var top = P(0, 1) - 1;
                var bottom = P(1, _rows) - 1;
                if (top < bottom && bottom < _rows) { _top = top; _bottom = bottom; }
                else { _top = 0; _bottom = _rows - 1; }
                _row = 0;
                _col = 0;
                break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
        }
        return i + 1;
    }

    private static int[] Params(string raw)
    {
        if (raw.Length == 0) return [];
        var parts = raw.Split(';');
        var result = new int[parts.Length];
        for (var k = 0; k < parts.Length; k++)
        {
            var part = parts[k];
            var colon = part.IndexOf(':');
            if (colon >= 0) part = part[..colon];
            result[k] = int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Min(v, 10000) : 0;
        }
        return result;
    }

    private void PrivateMode(int[] modes, bool on)
    {
        foreach (var m in modes)
        {
            if (m is not (1049 or 1047 or 47)) continue;
            if (on && _alt is null)
            {
                if (m == 1049) SaveCursor();
                _alt = NewScreen();
                if (m == 1049) { _row = 0; _col = 0; }
            }
            else if (!on && _alt is not null)
            {
                _alt = null;
                if (m == 1049) RestoreCursor();
            }
        }
    }

    /// <summary>Runs <paramref name="action"/> with the scroll region's top moved to <paramref name="top"/> (insert/delete lines).</summary>
    private void WithTop(int top, Action action)
    {
        var old = _top;
        _top = top;
        action();
        _top = old;
        _col = 0;
    }

    private void ScrollUpNoScrollback(int n)
    {
        var screen = Screen;
        for (var k = 0; k < n; k++)
        {
            for (var r = _top; r < _bottom; r++) screen[r] = screen[r + 1];
            screen[_bottom] = BlankLine();
        }
    }

    private void ReverseIndex()
    {
        _wrapPending = false;
        if (_row == _top) ScrollDown(1);
        else if (_row > 0) _row--;
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                Fill(_row, _col, _cols);
                for (var r = _row + 1; r < _rows; r++) Screen[r] = BlankLine();
                break;
            case 1:
                for (var r = 0; r < _row; r++) Screen[r] = BlankLine();
                Fill(_row, 0, Math.Min(_cols, _col + 1));
                break;
            case 2 or 3:
                // A full clear on the main screen keeps what was there as scrollback, as terminals do.
                if (_alt is null && mode == 2)
                {
                    var last = Array.FindLastIndex(_main, l => Text(l).Length > 0);
                    for (var r = 0; r <= last; r++) AddScrollback(_main[r]);
                }
                for (var r = 0; r < _rows; r++) Screen[r] = BlankLine();
                if (mode == 3) _scrollback.Clear();
                break;
        }
    }

    private void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: Fill(_row, _col, _cols); break;
            case 1: Fill(_row, 0, Math.Min(_cols, _col + 1)); break;
            case 2: Screen[_row] = BlankLine(); break;
        }
    }

    private void Fill(int row, int from, int to)
    {
        var line = Screen[row];
        for (var c = from; c < to; c++) line[c] = " ";
    }

    private void DeleteChars(int n)
    {
        var line = Screen[_row];
        n = Math.Min(n, _cols - _col);
        Array.Copy(line, _col + n, line, _col, _cols - _col - n);
        for (var c = _cols - n; c < _cols; c++) line[c] = " ";
    }

    private void InsertChars(int n)
    {
        var line = Screen[_row];
        n = Math.Min(n, _cols - _col);
        Array.Copy(line, _col, line, _col + n, _cols - _col - n);
        for (var c = _col; c < _col + n; c++) line[c] = " ";
    }

    private void SaveCursor() => (_savedRow, _savedCol) = (_row, _col);

    private void RestoreCursor()
    {
        _row = Math.Clamp(_savedRow, 0, _rows - 1);
        _col = Math.Clamp(_savedCol, 0, _cols - 1);
        _wrapPending = false;
    }

    private void Reset()
    {
        _alt = null;
        _main = NewScreen();
        _row = _col = _top = 0;
        _bottom = _rows - 1;
        _wrapPending = false;
    }

    private IReadOnlyList<string> Lines(int maxLines)
    {
        var lines = new List<string>();
        if (_alt is null) lines.AddRange(_scrollback);
        lines.AddRange(Screen.Select(Text));
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var lead = lines.FindIndex(l => l.Length > 0);
        if (lead > 0) lines.RemoveRange(0, lead);
        // An empty stretch of a full-screen layout (codex's viewport) would push the content out: keep one blank.
        for (var k = lines.Count - 1; k > 0; k--)
            if (lines[k].Length == 0 && lines[k - 1].Length == 0) lines.RemoveAt(k);
        return lines.Count > maxLines ? lines.GetRange(lines.Count - maxLines, maxLines) : lines;
    }

    /// <summary>Terminal cell width: 0 for combining marks, 2 for East Asian wide characters and most emoji.</summary>
    internal static int Width(Rune r)
    {
        var v = r.Value;
        var cat = Rune.GetUnicodeCategory(r);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        if (v is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF or >= 0xA000 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD)
            return 2;
        return 1;
    }
}
