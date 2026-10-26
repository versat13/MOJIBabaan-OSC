using System.Net;
using MOJIBabaan.Osc.Core;

namespace MOJIBabaan.Osc.Core.Tests;

/// <summary>32 セル（16 字 × 2 行）への対応と、20 セル（10 字 × 2 行）の旧アバター互換。</summary>
public class Capacity32Tests
{
    static readonly CharMap Map = CharMap.Default;

    static AvatarState Feed(BabaanText.Result r, AvatarState? st = null)
    {
        st ??= new AvatarState(Map);
        foreach (var m in BabaanMessages.BuildRegister(r)) st.Apply(m);
        return st;
    }

    // ---- エンコード ----

    [Fact]
    public void 既定は16字で32セル_メッセージは66個()
    {
        var r = BabaanText.ParseLines("あいう", "えお", Map);
        Assert.Equal(16, r.PerLine);
        Assert.Equal(32, r.Cells);
        var msgs = BabaanMessages.BuildRegister(r);
        Assert.Equal(66, msgs.Count);
        Assert.Equal("/avatar/parameters/BBN/O00h", msgs[0].Address);
        Assert.Equal("/avatar/parameters/BBN/O31l", msgs[63].Address);
        Assert.Equal("/avatar/parameters/BBN/OL1", msgs[64].Address);
        Assert.Equal(3, msgs[64].Args[0]);
        Assert.Equal("/avatar/parameters/BBN/OL2", msgs[65].Address);
        Assert.Equal(2, msgs[65].Args[0]);
    }

    [Fact]
    public void 二行目は16番のセルから()
    {
        var r = BabaanText.ParseLines("あ", "い", Map);
        Assert.Equal(2, r.Nos[0]);
        Assert.Equal(4, r.Nos[16]);
        var msgs = BabaanMessages.BuildRegister(r);
        Assert.Equal("/avatar/parameters/BBN/O16l", msgs[33].Address);
        Assert.Equal(4, msgs[33].Args[0]);
    }

    [Fact]
    public void 十六字ちょうどは超過なし_十七字目から超過()
    {
        var s16 = new string('あ', 16);
        var r = BabaanText.ParseLines(s16, "", Map);
        Assert.False(r.Over1);
        Assert.Equal(16, r.SendLen1);
        Assert.Equal(2, r.Nos[15]);
        Assert.Equal(0, r.Nos[16]);

        var r2 = BabaanText.ParseLines(s16 + "い", "", Map);
        Assert.True(r2.Over1);
        Assert.Equal("い", r2.Overflow1);
        Assert.Equal(16, r2.SendLen1);
        Assert.All(r2.Nos.Skip(16), n => Assert.Equal(0, n));        // 2 行目へは溢れない
    }

    [Fact]
    public void 二行目が超えたら切り捨てて超過を覚える()
    {
        var r = BabaanText.ParseLines("", new string('あ', 18), Map);
        Assert.True(r.Over2);
        Assert.Equal("ああ", r.Overflow2);
        Assert.Equal(16, r.SendLen2);
    }

    [Fact]
    public void 旧アバター互換_十字で20セル_メッセージは42個()
    {
        var r = BabaanText.ParseLines("あ", "い", Map, BabaanText.LegacyPerLine);
        Assert.Equal(20, r.Cells);
        var msgs = BabaanMessages.BuildRegister(r);
        Assert.Equal(42, msgs.Count);
        Assert.Equal("/avatar/parameters/BBN/O19l", msgs[39].Address);
        Assert.Equal("/avatar/parameters/BBN/OL1", msgs[40].Address);
        Assert.Equal(4, r.Nos[10]);
        Assert.True(BabaanText.ParseLines(new string('あ', 11), "", Map, 10).Over1);
    }

    // ---- 復元 ----

    [Fact]
    public void 三十二セルを復元する()
    {
        var r = BabaanText.ParseLines("ABCDEFGHIJKLMNOP", "ありがとうございました", Map);
        var st = Feed(r);
        Assert.Equal(16, st.PerLine);
        var got = st.Restored!;
        Assert.Equal("ABCDEFGHIJKLMNOP", got.Line1);
        Assert.Equal("ありがとうございました", got.Line2);
        Assert.Equal(16, got.PerLine);
        Assert.True(got.Matches(r));
    }

    [Fact]
    public void 三十二セルは途中までだと不明()
    {
        var st = new AvatarState(Map);
        foreach (var m in BabaanMessages.BuildRegister(BabaanText.ParseLines("あ", "", Map)).Take(60)) st.Apply(m);
        Assert.Null(st.Restored);
    }

    [Fact]
    public void 二十セルは容量が分かれば復元できる_分からなければ不明()
    {
        var r = BabaanText.ParseLines("5000兆円", "欲しい", Map, 10);
        var st = Feed(r);
        Assert.Null(st.PerLine);
        Assert.Null(st.Restored);                  // 20 セルか途中の 32 セルか区別できない
        Assert.True(st.SetCapacity(10));
        Assert.False(st.SetCapacity(10));
        var got = st.Restored!;
        Assert.Equal("5000兆円", got.Line1);
        Assert.Equal("欲しい", got.Line2);
        Assert.Equal(10, got.PerLine);
        Assert.True(got.Matches(r));
    }

    [Fact]
    public void 二十番以降のセルを受けたら必ず16字()
    {
        var st = new AvatarState(Map);
        st.SetCapacity(10);
        Feed(BabaanText.ParseLines("あ", "", Map), st);
        Assert.Equal(16, st.PerLine);
        Assert.NotNull(st.Restored);
    }

    [Fact]
    public void アバター変更で容量も忘れる()
    {
        var st = new AvatarState(Map);
        st.SetCapacity(10);
        st.Reset("avtr_x");
        Assert.Null(st.PerLine);
    }

    [Fact]
    public void 内容の一致は文字番号で比べる_未対応字も一致する()
    {
        var r = BabaanText.ParseLines("あΩい", "", Map);          // Ω は空白（0）で送られる
        var st = Feed(r);
        Assert.True(st.Restored!.Matches(r));
        Assert.False(st.Restored.Matches(BabaanText.ParseLines("あいう", "", Map)));
        Assert.False(st.Restored.Matches(BabaanText.ParseLines("あΩい", "", Map, 10)));   // 容量が違えば別
    }

    [Fact]
    public void OL1とOL2は16まで入る()
    {
        var r = BabaanText.ParseLines(new string('い', 16), new string('う', 16), Map);
        var st = Feed(r);
        Assert.Equal(new string('い', 16), st.Restored!.Line1);
        Assert.Equal(new string('う', 16), st.Restored.Line2);
    }

    // ---- 容量の判定 ----

    [Fact]
    public void キーから容量を判定()
    {
        Assert.Equal(16, AvatarCapacity.FromNames(new[] { "O00h", "O19h", "O31h" }));
        Assert.Equal(10, AvatarCapacity.FromNames(new[] { "O00h", "O19h", "OL1" }));
        Assert.Null(AvatarCapacity.FromNames(new[] { "O00h", "O05l", "Req" }));
        Assert.Null(AvatarCapacity.FromNames(Array.Empty<string>()));
    }

    static string Json(params string[] names)
        => "{\"FULL_PATH\":\"/avatar/parameters/BBN\",\"CONTENTS\":{"
           + string.Join(",", names.Select(n => $"\"{n}\":{{\"FULL_PATH\":\"/avatar/parameters/BBN/{n}\",\"TYPE\":\"i\",\"VALUE\":[0]}}"))
           + "}}";

    [Fact]
    public void OSCQueryの応答から容量を判定()
    {
        Assert.Equal(16, OscQueryJson.ParseBbn(Json("O00h", "O31h", "OL1")).PerLine);
        Assert.Equal(10, OscQueryJson.ParseBbn(Json("O00h", "O19h", "OL1")).PerLine);
        Assert.Null(OscQueryJson.ParseBbn(Json("O00h")).PerLine);
    }

    [Fact]
    public void OSC設定ファイルから容量を判定()
    {
        var root = Path.Combine(Path.GetTempPath(), "bbn-cap-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "usr_a", "Avatars");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "avtr_new.json"), "{\"parameters\":[{\"name\":\"BBN/O00h\"},{\"name\":\"BBN/O19h\"},{\"name\":\"BBN/O31h\"}]}");
            File.WriteAllText(Path.Combine(dir, "avtr_old.json"), "{\"parameters\":[{\"name\":\"BBN/O00h\"},{\"name\":\"BBN/O19h\"}]}");
            File.WriteAllText(Path.Combine(dir, "avtr_odd.json"), "{\"parameters\":[{\"name\":\"BBN/O00h\"}]}");
            File.WriteAllText(Path.Combine(dir, "avtr_none.json"), "{\"parameters\":[]}");

            Assert.Equal(new AvatarOscConfig.Info(true, 16), AvatarOscConfig.Inspect("avtr_new", root));
            Assert.Equal(new AvatarOscConfig.Info(true, 10), AvatarOscConfig.Inspect("avtr_old", root));
            Assert.Equal(new AvatarOscConfig.Info(true, null), AvatarOscConfig.Inspect("avtr_odd", root));
            Assert.Equal(new AvatarOscConfig.Info(false, null), AvatarOscConfig.Inspect("avtr_none", root));
            Assert.Null(AvatarOscConfig.Inspect("avtr_missing", root));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---- 配信の待ち時間 ----

    [Fact]
    public void 同期の目安は20セルで4_2秒_32セルで6_6秒_余裕1_2秒()
    {
        Assert.Equal(4.2, SyncTiming.Sync(20).TotalSeconds, 3);
        Assert.Equal(6.6, SyncTiming.Sync(32).TotalSeconds, 3);
        Assert.Equal(5.4, SyncTiming.Total(20).TotalSeconds, 3);
        Assert.Equal(7.8, SyncTiming.Total(32).TotalSeconds, 3);
    }
}

public class AutoWrapTests
{
    [Fact]
    public void 収まっていれば何もしない()
    {
        var w = AutoWrap.Wrap("あいう", "えお", 16, 3);
        Assert.False(w.Wrapped);
        Assert.Equal("あいう", w.Line1);
        Assert.Equal("えお", w.Line2);
        Assert.Equal(3, w.Caret);
    }

    [Fact]
    public void ちょうど上限は折り返さない_末尾の空白は数えない()
    {
        var s = new string('あ', 16);
        Assert.False(AutoWrap.Wrap(s, "", 16, 16).Wrapped);
        Assert.False(AutoWrap.Wrap(s + "  ", "", 16, 18).Wrapped);
    }

    [Fact]
    public void 末尾で超えたら超えた分を二行目の先頭へ_カーソルも二行目へ()
    {
        var w = AutoWrap.Wrap(new string('あ', 16) + "い", "", 16, 17);
        Assert.True(w.Wrapped);
        Assert.Equal(new string('あ', 16), w.Line1);
        Assert.Equal("い", w.Line2);
        Assert.True(w.CaretToLine2);
        Assert.Equal(1, w.Caret);          // 「い」の後ろ
    }

    [Fact]
    public void 二行目に文字があれば先頭へ足す()
    {
        var w = AutoWrap.Wrap(new string('あ', 10) + "いう", "えお", 10, 12);
        Assert.Equal(new string('あ', 10), w.Line1);
        Assert.Equal("いうえお", w.Line2);
        Assert.True(w.CaretToLine2);
        Assert.Equal(2, w.Caret);          // 「いう」の後ろ、「えお」の前
    }

    [Fact]
    public void 貼り付けで大きく超えても二行目へ送る()
    {
        var paste = "あいうえおかきくけこさしすせそたちつてと";     // 20 字
        var w = AutoWrap.Wrap(paste, "", 16, paste.Length);
        Assert.Equal(paste.Substring(0, 16), w.Line1);
        Assert.Equal(paste.Substring(16), w.Line2);
        Assert.True(w.CaretToLine2);
        Assert.Equal(4, w.Caret);
    }

    [Fact]
    public void 二行目も超えたら切らずに残す_送信時に切り捨て()
    {
        var w = AutoWrap.Wrap(new string('あ', 16) + "いう", new string('え', 16), 16, 18);
        Assert.Equal(18, w.Line2.Length);
        var r = BabaanText.ParseLines(w.Line1, w.Line2, CharMap.Default, 16);
        Assert.True(r.Over2);
        Assert.Equal(16, r.SendLen2);
    }

    [Fact]
    public void 途中に入力して押し出したときはカーソルを一行目に残す()
    {
        var w = AutoWrap.Wrap("X" + new string('あ', 16), "", 16, 1);
        Assert.True(w.Wrapped);
        Assert.Equal("X" + new string('あ', 15), w.Line1);
        Assert.Equal("あ", w.Line2);
        Assert.False(w.CaretToLine2);
        Assert.Equal(1, w.Caret);
    }

    [Fact]
    public void 十字行でも折り返す()
    {
        var w = AutoWrap.Wrap("あいうえおかきくけこさ", "", 10, 11);
        Assert.Equal("あいうえおかきくけこ", w.Line1);
        Assert.Equal("さ", w.Line2);
    }

    [Fact]
    public void サロゲートペアは一字として数える()
    {
        var s = string.Concat(Enumerable.Repeat("𠮷", 10)) + "あ";       // 𠮷 は UTF-16 で 2 つ
        var w = AutoWrap.Wrap(s, "", 10, s.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("𠮷", 10)), w.Line1);
        Assert.Equal("あ", w.Line2);
    }
}