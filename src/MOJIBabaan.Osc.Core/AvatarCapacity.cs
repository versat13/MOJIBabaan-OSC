namespace MOJIBabaan.Osc.Core;

/// <summary>
/// アバターの容量（1 行の字数）の判定。
/// BBN/O31h があれば 16 字/行（32 セル）、O19h までなら 10 字/行（20 セル）。
/// </summary>
public static class AvatarCapacity
{
    public const int Legacy = BabaanText.LegacyPerLine;
    public const int Full = BabaanText.FullPerLine;

    /// <summary>OSCQuery のキー（"O31h" など。BBN/ の後ろ）から判定する。判定できなければ null。</summary>
    public static int? FromNames(IEnumerable<string> names)
    {
        bool has19 = false;
        foreach (var n in names)
        {
            if (n == "O31h") return Full;
            if (n == "O19h") has19 = true;
        }
        return has19 ? Legacy : null;
    }

    /// <summary>OSC 設定ファイルの中身から判定する。判定できなければ null。</summary>
    public static int? FromConfigText(string text)
    {
        if (text.Contains("BBN/O31h", StringComparison.Ordinal)) return Full;
        if (text.Contains("BBN/O19h", StringComparison.Ordinal)) return Legacy;
        return null;
    }

    /// <summary>"O07h" のようなキーからセル番号を取り出す。該当しなければ -1。</summary>
    internal static int CellOfName(string name)
        => name.Length == 4 && name[0] == 'O' && char.IsDigit(name[1]) && char.IsDigit(name[2]) && (name[3] == 'h' || name[3] == 'l')
            ? (name[1] - '0') * 10 + (name[2] - '0')
            : -1;
}