using System.Text;
using MOJIBabaan.Osc.Core;

namespace MOJIBabaan.Osc.Core.Tests;

public class OscCodecTests
{
    static byte[] Hex(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();

    [Fact]
    public void Int_既知のバイト列()
    {
        // "/a" は "/a\0\0"、型タグ ",i\0\0"、値 00 00 00 01
        Assert.Equal(Hex("2F 61 00 00 2C 69 00 00 00 00 00 01"), OscCodec.Encode(new OscMessage("/a", 1)));
    }

    [Fact]
    public void Int_負の値と大きな値()
    {
        Assert.Equal(Hex("2F 61 00 00 2C 69 00 00 FF FF FF FF"), OscCodec.Encode(new OscMessage("/a", -1)));
        Assert.Equal(Hex("2F 61 00 00 2C 69 00 00 01 00 00 00"), OscCodec.Encode(new OscMessage("/a", 0x01000000)));
    }

    [Fact]
    public void Req17_アドレス26バイトは28バイトに詰める()
    {
        var addr = "/avatar/parameters/BBN/Req";
        Assert.Equal(26, addr.Length);
        var expected = Encoding.ASCII.GetBytes(addr).Concat(Hex("00 00 2C 69 00 00 00 00 00 11")).ToArray();
        Assert.Equal(expected, OscCodec.Encode(new OscMessage(addr, 17)));
    }

    [Fact]
    public void アドレスが4の倍数ちょうどなら終端で4バイト足す()
    {
        // "/abc" は 4 バイト。終端の 0 で 4 バイト追加され 8 バイトになる
        Assert.Equal(Hex("2F 61 62 63 00 00 00 00 2C 69 00 00 00 00 00 05"), OscCodec.Encode(new OscMessage("/abc", 5)));
    }

    [Fact]
    public void Bool_は型タグだけ()
    {
        Assert.Equal(Hex("2F 62 00 00 2C 54 00 00"), OscCodec.Encode(new OscMessage("/b", true)));
        Assert.Equal(Hex("2F 62 00 00 2C 46 00 00"), OscCodec.Encode(new OscMessage("/b", false)));
    }

    [Fact]
    public void Float_と_String()
    {
        Assert.Equal(Hex("2F 61 00 00 2C 66 00 00 3F 80 00 00"), OscCodec.Encode(new OscMessage("/a", 1.0f)));
        Assert.Equal(Hex("2F 61 00 00 2C 73 00 00 61 62 00 00"), OscCodec.Encode(new OscMessage("/a", "ab")));
        Assert.Equal(Hex("2F 61 00 00 2C 73 00 00 61 62 63 00"), OscCodec.Encode(new OscMessage("/a", "abc")));
        Assert.Equal(Hex("2F 61 00 00 2C 73 00 00 61 62 63 64 00 00 00 00"), OscCodec.Encode(new OscMessage("/a", "abcd")));
    }

    [Fact]
    public void 往復でそのまま戻る()
    {
        var m = new OscMessage("/avatar/parameters/BBN/O03h", 7, 0.5f, "avtr_x", true, false);
        var back = Assert.Single(OscCodec.Decode(OscCodec.Encode(m)));
        Assert.Equal(m.Address, back.Address);
        Assert.Equal(new object[] { 7, 0.5f, "avtr_x", true, false }, back.Args);
    }

    [Fact]
    public void 日本語の文字列も往復できる()
    {
        var m = new OscMessage("/avatar/change", "アバター");
        var back = Assert.Single(OscCodec.Decode(OscCodec.Encode(m)));
        Assert.Equal("アバター", back.Args[0]);
    }

    [Fact]
    public void バンドルを展開する()
    {
        var a = OscCodec.Encode(new OscMessage("/x", 1));
        var b = OscCodec.Encode(new OscMessage("/y", 2));
        var bundle = new List<byte>();
        bundle.AddRange(Encoding.ASCII.GetBytes("#bundle\0"));
        bundle.AddRange(new byte[8]);
        foreach (var e in new[] { a, b })
        {
            bundle.AddRange(new byte[] { 0, 0, 0, (byte)e.Length });
            bundle.AddRange(e);
        }
        var msgs = OscCodec.Decode(bundle.ToArray());
        Assert.Equal(2, msgs.Count);
        Assert.Equal("/x", msgs[0].Address);
        Assert.Equal(2, msgs[1].Args[0]);
    }

    [Fact]
    public void 型タグ無しのメッセージも読める()
    {
        var msgs = OscCodec.Decode(Hex("2F 61 00 00"));
        Assert.Equal("/a", Assert.Single(msgs).Address);
        Assert.Empty(msgs[0].Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("61 62 63 64")]                       // 終端なし
    [InlineData("2F 61 00 00 2C 69 00 00 00 00")]     // 値が足りない
    [InlineData("2F 61 00 00 69 00 00 00")]           // 型タグが , で始まらない
    [InlineData("2F 61 00 00 2C 7A 00 00")]           // 未対応の型
    [InlineData("23 62 75 6E 64 6C 65 00 00")]        // 短いバンドル
    public void 壊れたパケットは例外(string hex)
    {
        Assert.Throws<OscFormatException>(() => OscCodec.Decode(Hex(hex)));
    }

    [Fact]
    public void アドレスが不正なら送信前に例外()
    {
        Assert.Throws<ArgumentException>(() => OscCodec.Encode(new OscMessage("abc", 1)));
        Assert.Throws<ArgumentException>(() => OscCodec.Encode(new OscMessage("/a", 1L)));
    }
}