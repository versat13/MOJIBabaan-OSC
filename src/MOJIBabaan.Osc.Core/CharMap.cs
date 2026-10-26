using System.Text;
using System.Text.Json;

namespace MOJIBabaan.Osc.Core;

/// <summary>文字表。Chars の i 番目の文字が文字番号 i+1（0 は空白）。</summary>
public sealed class CharMap
{
    public string Chars { get; }
    public int Count => Chars.Length;

    public CharMap(string chars) => Chars = chars ?? "";

    /// <summary>文字番号を返す。表に無ければ 0。</summary>
    public int IndexOf(char c)
    {
        int i = Chars.IndexOf(c);
        return i < 0 ? 0 : i + 1;
    }

    /// <summary>文字番号から文字を返す。範囲外は null。</summary>
    public string? CharOf(int no) => no >= 1 && no <= Chars.Length ? Chars[no - 1].ToString() : null;

    public static CharMap FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new CharMap(doc.RootElement.GetProperty("chars").GetString() ?? "");
    }

    static CharMap? s_default;

    /// <summary>埋め込みリソース（Unity 側の BBN_CharMap.json のコピー）から読んだ文字表。</summary>
    public static CharMap Default => s_default ??= LoadEmbedded();

    static CharMap LoadEmbedded()
    {
        var asm = typeof(CharMap).Assembly;
        using var s = asm.GetManifestResourceStream("BBN_CharMap.json")
            ?? throw new InvalidOperationException("文字表のリソースが見つかりません。");
        using var r = new StreamReader(s, Encoding.UTF8);
        return FromJson(r.ReadToEnd());
    }
}