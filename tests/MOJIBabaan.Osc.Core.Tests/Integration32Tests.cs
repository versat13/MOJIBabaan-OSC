using System.Net;
using System.Net.Sockets;
using MOJIBabaan.Osc.Core;

namespace MOJIBabaan.Osc.Core.Tests;

/// <summary>32 セルの実 UDP 送信。</summary>
public class Integration32Tests
{
    static readonly CharMap Map = CharMap.Default;

    sealed class Listener : IDisposable
    {
        readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        public IPEndPoint EndPoint => (IPEndPoint)_udp.Client.LocalEndPoint!;
        public List<byte[]> Packets { get; } = new();
        public async Task ReceiveAsync(int count, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            while (Packets.Count < count) Packets.Add((await _udp.ReceiveAsync(cts.Token)).Buffer);
        }
        public List<OscMessage> Decoded() => Packets.SelectMany(p => OscCodec.Decode(p)).ToList();
        public void Dispose() => _udp.Dispose();
    }

    [Fact]
    public async Task 登録は66個の正しいパケットを1つずつ送る()
    {
        using var l = new Listener();
        using var s = new OscSender(l.EndPoint);
        var r = BabaanText.ParseLines("5000兆円欲しいABCDEFGHIJ", "お風呂が沸きました", Map);
        await new BabaanSender(s).RegisterAsync(r);
        await l.ReceiveAsync(66);

        var expected = BabaanMessages.BuildRegister(r);
        Assert.Equal(66, l.Packets.Count);
        for (int i = 0; i < 66; i++) Assert.Equal(OscCodec.Encode(expected[i]), l.Packets[i]);
        var msgs = l.Decoded();
        Assert.Equal(16, msgs[64].Args[0]);       // OL1
        Assert.Equal(9, msgs[65].Args[0]);        // OL2
    }

    [Fact]
    public async Task 登録して再生は66個_8秒待ち_Req17_Req0()
    {
        using var l = new Listener();
        using var s = new OscSender(l.EndPoint);
        var waits = new List<TimeSpan>();
        var bs = new BabaanSender(s, (t, ct) => { waits.Add(t); return Task.CompletedTask; });
        var counts = new List<int>();

        await bs.RegisterAndPlayAsync(BabaanText.ParseLines("お嬢様", "", Map), counts.Add);
        await l.ReceiveAsync(68);

        var msgs = l.Decoded();
        Assert.Equal(17, msgs[66].Args[0]);
        Assert.Equal(0, msgs[67].Args[0]);
        Assert.Equal(new[] { 8, 7, 6, 5, 4, 3, 2, 1, 0 }, counts);      // 7.8 秒を切り上げ
        Assert.Equal(8.0 + 0.3, waits.Sum(w => w.TotalSeconds), 3);
    }
}