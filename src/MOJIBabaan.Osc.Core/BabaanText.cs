using System.Text;

namespace MOJIBabaan.Osc.Core;

/// <summary>マスの状態。</summary>
public enum CellState
{
    /// <summary>空白（または文字が無い）。</summary>
    Empty,
    /// <summary>文字表にある字。</summary>
    Ok,
    /// <summary>文字表に無い字（空白になる）。</summary>
    Unsupported,
}

/// <summary>
/// 文言の正規化と文字番号への変換。Unity 側の BabaanText.cs と同じ規則。
/// 1 行の字数（perLine）は 16（32 セル）か、旧アバター互換の 10（20 セル）。
/// </summary>
public static class BabaanText
{
    /// <summary>旧アバター（20 セル）の 1 行の字数。</summary>
    public const int LegacyPerLine = 10;
    /// <summary>現行アバター（32 セル）の 1 行の字数。</summary>
    public const int FullPerLine = 16;
    public const int MaxLines = 2;
    public const int MaxCells = FullPerLine * MaxLines;

    public sealed class Result
    {
        public Result(int perLine)
        {
            PerLine = perLine;
            Nos = new int[Cells];
            Chars = Enumerable.Repeat("", Cells).ToArray();
            States = new CellState[Cells];
        }

        /// <summary>1 行の字数（10 か 16）。</summary>
        public int PerLine { get; }
        /// <summary>セル数（PerLine × 2）。</summary>
        public int Cells => PerLine * MaxLines;
        public bool IsEmpty;
        /// <summary>切り捨て前の文字数（行末の空白は除く）。</summary>
        public int Len1, Len2;
        public bool TooManyLines;
        /// <summary>セルごとの文字番号（0 は空白）。0..PerLine-1 が 1 行目、続く PerLine 個が 2 行目。</summary>
        public int[] Nos;
        /// <summary>セルごとの表示文字（正規化後。空白は空文字）。</summary>
        public string[] Chars;
        public CellState[] States;
        public List<string> Unsupported = new();
        /// <summary>PerLine 文字目より後ろ（送られない部分）。</summary>
        public string Overflow1 = "", Overflow2 = "";
        public bool Over1 => Len1 > PerLine;
        public bool Over2 => Len2 > PerLine;
        public string Line1Text = "", Line2Text = "";
        public string Label => string.Join(" ", new[] { Line1Text.Trim(), Line2Text.Trim() }.Where(x => x.Length > 0));
        /// <summary>OL1 / OL2 に入れる文字数（0〜PerLine）。</summary>
        public int SendLen1 => Math.Min(Len1, PerLine);
        public int SendLen2 => Math.Min(Len2, PerLine);
    }

    /// <summary>全角英数字・全角スペースは半角へ、半角カナは全角カナへ。それ以外は変えない。</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '　') sb.Append(' ');
            else if ((c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ') || (c >= '０' && c <= '９')) sb.Append((char)(c - 0xFEE0));
            else if (c >= '｡' && c <= 'ﾟ')
            {
                if (i + 1 < s.Length && (s[i + 1] == 'ﾞ' || s[i + 1] == 'ﾟ'))
                {
                    var two = (c.ToString() + s[i + 1]).Normalize(NormalizationForm.FormKC);
                    if (two.Length == 1) { sb.Append(two); i++; continue; }
                }
                sb.Append(c.ToString().Normalize(NormalizationForm.FormKC));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // 1 文字（サロゲートペアは 1 つ）ずつに分ける
    internal static List<string> SplitCells(string line)
    {
        var l = new List<string>();
        for (int i = 0; i < line.Length; i++)
        {
            if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length) { l.Add(line.Substring(i, 2)); i++; }
            else l.Add(line[i].ToString());
        }
        return l;
    }

    /// <summary>入力欄の 2 行（改行は空白に直す）から結果を作る。</summary>
    public static Result ParseLines(string? line1, string? line2, CharMap map, int perLine = FullPerLine)
        => Parse(Flatten(line1) + "\n" + Flatten(line2), map, perLine);

    static string Flatten(string? s) => (s ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    public static Result Parse(string? raw, CharMap map, int perLine = FullPerLine)
    {
        var r = new Result(perLine);
        var lines = Normalize(raw).TrimEnd('\n').Split('\n');
        r.TooManyLines = lines.Length > MaxLines;
        var c1 = SplitCells(lines[0].TrimEnd(' '));
        var c2 = lines.Length > 1 ? SplitCells(lines[1].TrimEnd(' ')) : new List<string>();
        r.Len1 = c1.Count; r.Len2 = c2.Count;
        r.IsEmpty = c1.All(x => x == " ") && c2.All(x => x == " ");
        for (int i = 0; i < r.Cells; i++)
        {
            var src = i < perLine ? c1 : c2;
            int col = i % perLine;
            if (col >= src.Count) continue;
            string cell = src[col];
            if (cell == " ") continue;                       // 空白は 0
            int no = map != null && cell.Length == 1 ? map.IndexOf(cell[0]) : 0;
            r.Nos[i] = no;
            r.Chars[i] = Printable(cell);
            r.States[i] = no == 0 ? CellState.Unsupported : CellState.Ok;
            if (no == 0 && !r.Unsupported.Contains(cell)) r.Unsupported.Add(cell);
        }
        r.Line1Text = string.Concat(c1.Take(perLine)).Trim();
        r.Line2Text = string.Concat(c2.Take(perLine)).Trim();
        r.Overflow1 = string.Concat(c1.Skip(perLine));
        r.Overflow2 = string.Concat(c2.Skip(perLine));
        return r;
    }

    /// <summary>表示用。制御文字は U+XXXX にする。</summary>
    public static string Printable(string cell) => cell.Length == 1 && char.IsControl(cell[0]) ? "U+" + ((int)cell[0]).ToString("X4") : cell;
}