namespace MOJIBabaan.Osc.Core;

/// <summary>入力の自動折り返しの結果。Caret は CaretToLine2 が true なら 2 行目、false なら 1 行目の位置。</summary>
public readonly record struct WrapResult(string Line1, string Line2, bool Wrapped, bool CaretToLine2, int Caret);

/// <summary>1 行目が上限を超えたら、超えた分を 2 行目の先頭へ送る。</summary>
public static class AutoWrap
{
    /// <summary>
    /// 1 行目が perLine 字（行末の空白は数えない）を超えていれば、超えた分を 2 行目の先頭へ送る。
    /// caret は 1 行目のカーソル位置（UTF-16 の添字）。カーソルが切れ目より後ろなら 2 行目へ移す。
    /// 2 行目が超えても切らない（送信時に切り捨て、画面では赤で示す）。
    /// </summary>
    public static WrapResult Wrap(string? line1, string? line2, int perLine, int caret)
    {
        line1 ??= ""; line2 ??= "";
        caret = Math.Clamp(caret, 0, line1.Length);
        var effective = line1.TrimEnd(' ');
        int cut = CutIndex(line1, perLine);
        if (cut < 0 || cut >= effective.Length) return new WrapResult(line1, line2, false, false, caret);

        string overflow = line1.Substring(cut);
        string head = line1.Substring(0, cut);
        string tail = overflow + line2;
        if (caret > cut) return new WrapResult(head, tail, true, true, Math.Min(caret - cut, overflow.Length));
        return new WrapResult(head, tail, true, false, caret);
    }

    // perLine 字（サロゲートペアは 1 字）の直後の添字。それより長くなければ -1。
    static int CutIndex(string s, int perLine)
    {
        int cells = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (cells == perLine) return i;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) i++;
            cells++;
        }
        return -1;
    }
}