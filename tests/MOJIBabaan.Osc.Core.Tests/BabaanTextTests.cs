using MOJIBabaan.Osc.Core;

namespace MOJIBabaan.Osc.Core.Tests;

public class BabaanTextTests
{
    static readonly CharMap Map = CharMap.Default;

    [Fact]
    public void CharMap_埋め込みリソースが読める()
    {
        Assert.Equal(3622, Map.Count);
        Assert.Equal(1, Map.IndexOf('ぁ'));
        Assert.Equal(2, Map.IndexOf('あ'));
        Assert.Equal("あ", Map.CharOf(2));
        Assert.Equal(0, Map.IndexOf('Ω'));
        Assert.Null(Map.CharOf(0));
        Assert.Null(Map.CharOf(3623));
        Assert.True(Map.IndexOf('A') > 0);
        Assert.True(Map.IndexOf('嬢') > 0);
    }

    [Theory]
    [InlineData("ＡＢＣ１２３ａｚ", "ABC123az")]
    [InlineData("あ　い", "あ い")]
    [InlineData("ｶﾞｷﾞｸﾞﾊﾟｱ", "ガギグパア")]
    [InlineData("ﾊﾟﾋﾟﾌﾟ", "パピプ")]
    [InlineData("ｱｲｳｴｵ", "アイウエオ")]
    [InlineData("ｰ｡｢｣､･", "ー。「」、・")]
    [InlineData("お嬢様", "お嬢様")]
    [InlineData("ABC！？", "ABC！？")]       // 全角記号は変えない
    public void 正規化(string input, string expected)
    {
        Assert.Equal(expected, BabaanText.Normalize(input));
    }

    [Fact]
    public void 正規化_改行をそろえる()
    {
        Assert.Equal("a\nb\nc", BabaanText.Normalize("a\r\nb\rc"));
        Assert.Equal("", BabaanText.Normalize(null));
    }

    [Fact]
    public void 未対応文字は番号0で赤マス扱い()
    {
        var r = BabaanText.ParseLines("あΩい", "", Map);
        Assert.Equal(new[] { 2, 0, 4 }, r.Nos.Take(3).ToArray());
        Assert.Equal(CellState.Ok, r.States[0]);
        Assert.Equal(CellState.Unsupported, r.States[1]);
        Assert.Equal(CellState.Ok, r.States[2]);
        Assert.Equal(CellState.Empty, r.States[3]);
        Assert.Equal(new[] { "Ω" }, r.Unsupported);
    }

    [Fact]
    public void 全角英数は半角にそろえて番号化()
    {
        var r = BabaanText.ParseLines("Ａ", "", Map);
        Assert.Equal(Map.IndexOf('A'), r.Nos[0]);
        Assert.Equal("A", r.Chars[0]);
    }

    [Fact]
    public void 空白は0で行末の空白は数えない()
    {
        var r = BabaanText.ParseLines("あ い  ", "", Map);
        Assert.Equal(3, r.Len1);
        Assert.Equal(0, r.Nos[1]);
        Assert.Equal(CellState.Empty, r.States[1]);
        Assert.False(r.IsEmpty);
        Assert.True(BabaanText.ParseLines("  ", "", Map).IsEmpty);
        Assert.True(BabaanText.ParseLines("", "", Map).IsEmpty);
    }

    [Fact]
    public void セル化_2行目は10番から()
    {
        var r = BabaanText.ParseLines("あ", "い", Map);
        Assert.Equal(2, r.Nos[0]);
        Assert.Equal(4, r.Nos[10]);
        Assert.Equal(1, r.Len1);
        Assert.Equal(1, r.Len2);
    }

    [Fact]
    public void 行頭が空でも2行目は2行目()
    {
        var r = BabaanText.ParseLines("", "あ", Map);
        Assert.Equal(0, r.Nos[0]);
        Assert.Equal(2, r.Nos[10]);
        Assert.Equal(0, r.Len1);
    }

    [Fact]
    public void 超過は切り捨てて超過分を覚える()
    {
        var r = BabaanText.ParseLines("あいうえおかきくけこさし", "", Map);
        Assert.Equal(12, r.Len1);
        Assert.True(r.Over1);
        Assert.Equal("さし", r.Overflow1);
        Assert.Equal(10, r.SendLen1);
        Assert.All(r.Nos.Skip(10), n => Assert.Equal(0, n));   // 2 行目へ溢れない
        Assert.Equal(Map.IndexOf('こ'), r.Nos[9]);
    }

    [Fact]
    public void 入力欄の改行は空白になる()
    {
        var r = BabaanText.ParseLines("あ\nい", "", Map);
        Assert.Equal(3, r.Len1);
        Assert.Equal(0, r.Len2);
    }

    [Fact]
    public void 文字番号からhとlへ()
    {
        // 文字番号 300 の文字を入れると h=1, l=44
        var ch = Map.CharOf(300)!;
        var r = BabaanText.ParseLines(ch, "", Map);
        Assert.Equal(300, r.Nos[0]);
        var msgs = BabaanMessages.BuildRegister(r);
        Assert.Equal("/avatar/parameters/BBN/O00h", msgs[0].Address);
        Assert.Equal(1, msgs[0].Args[0]);
        Assert.Equal("/avatar/parameters/BBN/O00l", msgs[1].Address);
        Assert.Equal(44, msgs[1].Args[0]);

        // 最大番号 3622 = 14*256 + 38
        var last = Map.CharOf(3622)!;
        var r2 = BabaanText.ParseLines(last, "", Map);
        var m2 = BabaanMessages.BuildRegister(r2);
        Assert.Equal(14, m2[0].Args[0]);
        Assert.Equal(38, m2[1].Args[0]);
    }

    [Fact]
    public void 登録メッセージは42個で順番どおり()
    {
        var r = BabaanText.ParseLines("あいう", "えお", Map);
        var msgs = BabaanMessages.BuildRegister(r);
        Assert.Equal(42, msgs.Count);
        Assert.Equal("/avatar/parameters/BBN/O00h", msgs[0].Address);
        Assert.Equal("/avatar/parameters/BBN/O09l", msgs[19].Address);
        Assert.Equal("/avatar/parameters/BBN/O10h", msgs[20].Address);
        Assert.Equal("/avatar/parameters/BBN/O19l", msgs[39].Address);
        Assert.Equal("/avatar/parameters/BBN/OL1", msgs[40].Address);
        Assert.Equal(3, msgs[40].Args[0]);
        Assert.Equal("/avatar/parameters/BBN/OL2", msgs[41].Address);
        Assert.Equal(2, msgs[41].Args[0]);
        Assert.All(msgs, m => Assert.IsType<int>(m.Args[0]));
    }
}