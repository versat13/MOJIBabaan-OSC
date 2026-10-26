using System.Text.Json;
using MOJIBabaan.Osc.Core;

namespace MOJIBabaan.Osc.Core.Tests;

public class AvatarStateTests
{
    static readonly CharMap Map = CharMap.Default;

    static IEnumerable<OscMessage> AsReceived(BabaanText.Result r)
        => BabaanMessages.BuildRegister(r);

    [Fact]
    public void 受信値から文言を復元する()
    {
        var r = BabaanText.ParseLines("5000兆円", "欲しい", Map);
        var st = new AvatarState(Map);
        Assert.Null(st.Restored);
        foreach (var m in AsReceived(r)) st.Apply(m);
        var got = st.Restored;
        Assert.NotNull(got);
        Assert.Equal("5000兆円", got!.Line1);
        Assert.Equal("欲しい", got.Line2);
        Assert.False(got.HasUnknown);
        Assert.True(st.SeenBbn);
    }

    [Fact]
    public void 一部しか受け取っていなければ不明()
    {
        var r = BabaanText.ParseLines("あい", "", Map);
        var st = new AvatarState(Map);
        foreach (var m in AsReceived(r).Take(39)) st.Apply(m);
        Assert.Null(st.Restored);
        Assert.True(st.SeenBbn);
    }

    [Fact]
    public void 途中の空白と未対応文字は空白で復元される()
    {
        var r = BabaanText.ParseLines("あ い", "", Map);
        var st = new AvatarState(Map);
        foreach (var m in AsReceived(r)) st.Apply(m);
        Assert.Equal("あ い", st.Restored!.Line1);
    }

    [Fact]
    public void 文字数OLより後ろは無視する()
    {
        var st = new AvatarState(Map);
        foreach (var m in AsReceived(BabaanText.ParseLines("あいう", "", Map))) st.Apply(m);
        st.Apply(OscMessage.Int("/avatar/parameters/BBN/OL1", 2));
        Assert.Equal("あい", st.Restored!.Line1);
    }

    [Fact]
    public void 表に無い番号は疑問符で不明扱い()
    {
        var st = new AvatarState(Map);
        foreach (var m in AsReceived(BabaanText.ParseLines("", "", Map))) st.Apply(m);
        st.Apply(OscMessage.Int("/avatar/parameters/BBN/O00h", 15));   // 15*256 > 3622
        st.Apply(OscMessage.Int("/avatar/parameters/BBN/OL1", 1));
        Assert.Equal("?", st.Restored!.Line1);
        Assert.True(st.Restored.HasUnknown);
    }

    [Fact]
    public void アバター変更でリセット()
    {
        var st = new AvatarState(Map);
        foreach (var m in AsReceived(BabaanText.ParseLines("あ", "", Map))) st.Apply(m);
        Assert.NotNull(st.Restored);
        Assert.True(st.Apply(new OscMessage("/avatar/change", "avtr_new")));
        Assert.Null(st.Restored);
        Assert.False(st.SeenBbn);
        Assert.Equal("avtr_new", st.AvatarId);
    }

    [Fact]
    public void BBN以外のパラメータは無視()
    {
        var st = new AvatarState(Map);
        Assert.False(st.Apply(OscMessage.Int("/avatar/parameters/VelocityX", 1)));
        Assert.False(st.SeenBbn);
        Assert.True(st.Apply(OscMessage.Int("/avatar/parameters/BBN/Ptr", 3)));
        Assert.True(st.SeenBbn);
    }

    [Fact]
    public void Bool_と_Float_の値も受ける()
    {
        var st = new AvatarState(Map);
        Assert.True(st.Apply(new OscMessage("/avatar/parameters/BBN/Map1", true)));
        st.Apply(new OscMessage("/avatar/parameters/BBN/O00l", 2.0f));
        Assert.True(st.SeenBbn);
    }
}

public class OscQueryJsonTests
{
    const string Sample = """
    {"DESCRIPTION":"BBN","FULL_PATH":"/avatar/parameters/BBN","ACCESS":0,"CONTENTS":{
      "O00h":{"DESCRIPTION":"O00h","FULL_PATH":"/avatar/parameters/BBN/O00h","ACCESS":3,"TYPE":"i","VALUE":[0]},
      "O00l":{"FULL_PATH":"/avatar/parameters/BBN/O00l","ACCESS":3,"TYPE":"i","VALUE":[2]},
      "OL1":{"FULL_PATH":"/avatar/parameters/BBN/OL1","ACCESS":3,"TYPE":"i","VALUE":[1]},
      "Map1":{"FULL_PATH":"/avatar/parameters/BBN/Map1","ACCESS":3,"TYPE":"T","VALUE":[true]}
    }}
    """;

    [Fact]
    public void BBN以下の値を取り出す()
    {
        var q = OscQueryJson.ParseBbn(Sample);
        Assert.True(q.HasBbn);
        Assert.Equal(0, q.Values["O00h"]);
        Assert.Equal(2, q.Values["O00l"]);
        Assert.Equal(1, q.Values["OL1"]);
        Assert.Equal(1, q.Values["Map1"]);
    }

    [Fact]
    public void BBNが無ければ未検出()
    {
        var q = OscQueryJson.ParseBbn("""{"FULL_PATH":"/avatar/parameters","CONTENTS":{"Foo":{"FULL_PATH":"/avatar/parameters/Foo","VALUE":[1]}}}""");
        Assert.False(q.HasBbn);
        Assert.Empty(q.Values);
    }

    [Fact]
    public void 入れ子のCONTENTSも読む_と_状態へ反映()
    {
        var json = """{"FULL_PATH":"/avatar/parameters","CONTENTS":{"BBN":{"FULL_PATH":"/avatar/parameters/BBN","CONTENTS":{"O01h":{"FULL_PATH":"/avatar/parameters/BBN/O01h","VALUE":[1]}}}}}""";
        var q = OscQueryJson.ParseBbn(json);
        Assert.True(q.HasBbn);
        Assert.Equal(1, q.Values["O01h"]);
        var st = new AvatarState(CharMap.Default);
        Assert.True(st.ApplyValues(q.Values, q.HasBbn));
        Assert.True(st.SeenBbn);
    }

    [Fact]
    public void アバターIDを取り出す()
    {
        Assert.Equal("avtr_1234", OscQueryJson.ParseAvatarId("""{"FULL_PATH":"/avatar/change","TYPE":"s","VALUE":["avtr_1234"]}"""));
        Assert.Null(OscQueryJson.ParseAvatarId("""{"FULL_PATH":"/avatar/change"}"""));
    }

    [Fact]
    public void 壊れたJSONは例外()
    {
        Assert.ThrowsAny<JsonException>(() => OscQueryJson.ParseBbn("{not json"));
    }
}

public class AvatarOscConfigTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bbn-osc-test-" + Guid.NewGuid().ToString("N"));

    void Write(string user, string avatar, string body)
    {
        var dir = Path.Combine(_root, user, "Avatars");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, avatar + ".json"), body);
    }

    [Fact]
    public void 設定ファイルにBBNがあれば対応()
    {
        Write("usr_a", "avtr_yes", """{"parameters":[{"name":"BBN/O00h","input":{"address":"/avatar/parameters/BBN/O00h","type":"Int"}}]}""");
        Write("usr_a", "avtr_no", """{"parameters":[{"name":"Other"}]}""");
        Assert.True(AvatarOscConfig.HasBbn("avtr_yes", _root));
        Assert.False(AvatarOscConfig.HasBbn("avtr_no", _root));
        Assert.Null(AvatarOscConfig.HasBbn("avtr_missing", _root));
        Assert.Null(AvatarOscConfig.HasBbn("avtr_yes", Path.Combine(_root, "nothing")));
        Assert.Null(AvatarOscConfig.HasBbn("..\\evil", _root));
    }

    [Fact]
    public void 複数ユーザーのフォルダを探す()
    {
        Write("usr_b", "avtr_x", """{"parameters":[]}""");
        Write("usr_c", "avtr_x", """{"parameters":[{"name":"BBN/O00h"}]}""");
        Assert.True(AvatarOscConfig.HasBbn("avtr_x", _root));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

public class HistoryStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bbn-hist-test-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "history.json");

    [Fact]
    public void 追加は新しい順で同じ文言は先頭へ移る()
    {
        var h = new HistoryStore(null);
        h.Add("a", "");
        h.Add("b", "c");
        h.Add("a", "");
        Assert.Equal(new[] { "a", "b" }, h.Items.Select(x => x.Line1).ToArray());
        Assert.Null(h.Add("", ""));
        Assert.Equal(2, h.Items.Count);
    }

    [Fact]
    public void 上限50件_ピン留めは数えない()
    {
        var h = new HistoryStore(null);
        var pinned = h.Add("pin", "")!;
        h.SetPinned(pinned, true);
        for (int i = 0; i < 60; i++) h.Add("n" + i, "");
        Assert.Equal(50, h.Recent.Count());
        Assert.Single(h.Pinned);
        Assert.Equal("n59", h.Items[0].Line1);
        Assert.DoesNotContain(h.Items, x => x.Line1 == "n0");          // 古い方が消える
        Assert.Contains(h.Items, x => x.Line1 == "pin");               // ピンは残る
        Assert.Equal(51, h.Items.Count);
    }

    [Fact]
    public void ピンを外すと上限に数えられ古いものから消える()
    {
        var h = new HistoryStore(null);
        for (int i = 0; i < 50; i++) h.Add("n" + i, "");
        var oldest = h.Items.Last();
        h.SetPinned(oldest, true);
        h.Add("new", "");                       // ピン以外が 50 件に収まる
        Assert.Equal(51, h.Items.Count);
        h.SetPinned(oldest, false);             // 51 件目になるので消える
        Assert.Equal(50, h.Items.Count);
        Assert.DoesNotContain(h.Items, x => x.Line1 == "n0");
    }

    [Fact]
    public void 保存と読み込み()
    {
        var h = new HistoryStore(FilePath);
        h.Add("お嬢様", "");
        var x = h.Add("5000兆円", "欲しい")!;
        h.SetPinned(x, true);

        Assert.Contains("5000兆円", File.ReadAllText(FilePath));      // 日本語はエスケープしない
        var h2 = new HistoryStore(FilePath);
        h2.Load();
        Assert.Equal(2, h2.Items.Count);
        Assert.Equal("5000兆円", h2.Items[0].Line1);
        Assert.Equal("欲しい", h2.Items[0].Line2);
        Assert.True(h2.Items[0].Pinned);
        Assert.False(h2.Items[1].Pinned);
    }

    [Fact]
    public void ファイルが無ければ空()
    {
        var h = new HistoryStore(FilePath);
        h.Load();
        Assert.Empty(h.Items);
    }

    [Fact]
    public void 壊れたファイルは退避して空で始める()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{broken");
        var h = new HistoryStore(FilePath);
        h.Load();
        Assert.Empty(h.Items);
        Assert.True(File.Exists(FilePath + ".bak"));
        h.Add("x", "");
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void 削除できる()
    {
        var h = new HistoryStore(FilePath);
        var a = h.Add("a", "")!;
        h.Add("b", "");
        h.Remove(a);
        Assert.Equal(new[] { "b" }, h.Items.Select(x => x.Line1).ToArray());
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}

public class AppSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bbn-set-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 既定値と保存読み込み()
    {
        var path = Path.Combine(_dir, "settings.json");
        var def = AppSettings.Load(path);
        Assert.Equal("127.0.0.1", def.SendHost);
        Assert.Equal(9000, def.SendPort);
        Assert.Equal(9001, def.ReceivePort);
        Assert.True(def.UseOscQuery);

        def.SendPort = 9100; def.ReceivePort = 9101; def.UseOscQuery = false;
        def.Save(path);
        var back = AppSettings.Load(path);
        Assert.Equal(9100, back.SendPort);
        Assert.Equal(9101, back.ReceivePort);
        Assert.False(back.UseOscQuery);

        File.WriteAllText(path, "{broken");
        Assert.Equal(9000, AppSettings.Load(path).SendPort);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}