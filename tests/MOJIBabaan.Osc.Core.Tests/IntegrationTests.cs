using System.Net;
using System.Net.Sockets;
using MOJIBabaan.Osc.Core;

namespace MOJIBabaan.Osc.Core.Tests;

/// <summary>実際の UDP（127.0.0.1）を使った結合テスト。</summary>
public class IntegrationTests
{
    static readonly CharMap Map = CharMap.Default;

    // 送信先の代わりに待ち受ける UDP。受けたパケットを全部ためる。
    sealed class Listener : IDisposable
    {
        readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        public IPEndPoint EndPoint => (IPEndPoint)_udp.Client.LocalEndPoint!;
        public List<byte[]> Packets { get; } = new();

        public async Task ReceiveAsync(int count, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            while (Packets.Count < count)
            {
                var r = await _udp.ReceiveAsync(cts.Token);
                Packets.Add(r.Buffer);
            }
        }

        public List<OscMessage> Decoded() => Packets.SelectMany(p => OscCodec.Decode(p)).ToList();
        public void Dispose() => _udp.Dispose();
    }

    [Fact]
    public async Task 登録は42個の正しいパケットを1つずつ送る()
    {
        using var l = new Listener();
        using var s = new OscSender(l.EndPoint);
        var bs = new BabaanSender(s);
        var r = BabaanText.ParseLines("5000兆円", "欲しい", Map, BabaanText.LegacyPerLine);

        await bs.RegisterAsync(r);
        await l.ReceiveAsync(42);

        Assert.Equal(42, l.Packets.Count);
        var expected = BabaanMessages.BuildRegister(r);
        for (int i = 0; i < 42; i++)
            Assert.Equal(OscCodec.Encode(expected[i]), l.Packets[i]);     // 1 パケット 1 メッセージ、バイト列まで一致
        var msgs = l.Decoded();
        Assert.Equal(6, msgs[40].Args[0]);                                // OL1 = 「5000兆円」の 6 文字
        Assert.Equal(3, msgs[41].Args[0]);                                // OL2 = 「欲しい」の 3 文字
    }

    [Fact]
    public async Task 再生はReq17のあと0を送る()
    {
        using var l = new Listener();
        using var s = new OscSender(l.EndPoint);
        var waits = new List<TimeSpan>();
        var bs = new BabaanSender(s, (t, ct) => { waits.Add(t); return Task.CompletedTask; });

        await bs.PlayAsync();
        await l.ReceiveAsync(2);

        var msgs = l.Decoded();
        Assert.Equal("/avatar/parameters/BBN/Req", msgs[0].Address);
        Assert.Equal(17, msgs[0].Args[0]);
        Assert.Equal(0, msgs[1].Args[0]);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(300) }, waits);
    }

    [Fact]
    public async Task 登録して再生は42個_6秒待ち_Req17_Req0()
    {
        using var l = new Listener();
        using var s = new OscSender(l.EndPoint);
        var waits = new List<TimeSpan>();
        var bs = new BabaanSender(s, (t, ct) => { waits.Add(t); return Task.CompletedTask; });
        var counts = new List<int>();

        await bs.RegisterAndPlayAsync(BabaanText.ParseLines("お嬢様", "", Map, BabaanText.LegacyPerLine), counts.Add);
        await l.ReceiveAsync(44);

        var msgs = l.Decoded();
        Assert.Equal(44, msgs.Count);
        Assert.Equal("/avatar/parameters/BBN/OL2", msgs[41].Address);
        Assert.Equal(17, msgs[42].Args[0]);
        Assert.Equal(0, msgs[43].Args[0]);
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1, 0 }, counts);
        Assert.Equal(6.0 + 0.3, waits.Sum(w => w.TotalSeconds), 3);
    }

    [Fact]
    public async Task 再生を中断してもReq0は送る()
    {
        using var l = new Listener();
        using var s = new OscSender(l.EndPoint);
        var bs = new BabaanSender(s, (t, ct) => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => bs.PlayAsync());
        await l.ReceiveAsync(2);
        Assert.Equal(0, l.Decoded()[1].Args[0]);
    }

    [Fact]
    public async Task 受信器はVRChat役の送信を受けて文言を復元できる()
    {
        using var rx = new OscReceiver();
        rx.Start(0);                                          // 空いているポート（9001 相当）
        var state = new AvatarState(Map);
        var done = new TaskCompletionSource<RestoredText>();
        rx.MessageReceived += m =>
        {
            lock (state)
            {
                state.Apply(m);
                if (state.Restored is { } t) done.TrySetResult(t);
            }
        };

        using var fakeVrc = new OscSender(new IPEndPoint(IPAddress.Loopback, rx.Port));
        await fakeVrc.SendAsync(new OscMessage("/avatar/change", "avtr_test"));
        await fakeVrc.SendAsync(BabaanMessages.BuildRegister(BabaanText.ParseLines("お風呂が", "沸きました", Map)));

        var got = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("お風呂が", got.Line1);
        Assert.Equal("沸きました", got.Line2);
        Assert.Equal("avtr_test", state.AvatarId);
    }

    [Fact]
    public async Task 受信器はバンドルと壊れたパケットを扱える()
    {
        using var rx = new OscReceiver();
        rx.Start(0);
        var got = new List<OscMessage>();
        var errors = 0;
        var two = new TaskCompletionSource();
        rx.MessageReceived += m => { lock (got) { got.Add(m); if (got.Count == 2) two.TrySetResult(); } };
        rx.PacketError += _ => Interlocked.Increment(ref errors);

        using var udp = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Loopback, rx.Port);
        await udp.SendAsync(new byte[] { 1, 2, 3 }, 3, ep);                    // 壊れている
        var a = OscCodec.Encode(new OscMessage("/avatar/parameters/BBN/O00l", 5));
        var b = OscCodec.Encode(new OscMessage("/avatar/parameters/BBN/O01l", 6));
        var bundle = new List<byte>();
        bundle.AddRange(System.Text.Encoding.ASCII.GetBytes("#bundle\0"));
        bundle.AddRange(new byte[8]);
        foreach (var e in new[] { a, b }) { bundle.AddRange(new byte[] { 0, 0, 0, (byte)e.Length }); bundle.AddRange(e); }
        await udp.SendAsync(bundle.ToArray(), bundle.Count, ep);

        await two.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, got.Count);
        Assert.Equal(1, Volatile.Read(ref errors));
    }

    [Fact]
    public void 使用中のポートでは受信を始められない()
    {
        using var rx1 = new OscReceiver();
        rx1.Start(0);
        using var rx2 = new OscReceiver();
        Assert.Throws<SocketException>(() => rx2.Start(rx1.Port));
    }

    [Fact]
    public void 送信先の解決()
    {
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), OscSender.ResolveTarget("127.0.0.1", 9000));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.5"), 9100), OscSender.ResolveTarget(" 192.168.1.5 ", 9100));
        Assert.Throws<ArgumentOutOfRangeException>(() => OscSender.ResolveTarget("127.0.0.1", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OscSender.ResolveTarget("127.0.0.1", 70000));
    }
}