using System.Net;
using System.Net.Sockets;

namespace MOJIBabaan.Osc.Core;

/// <summary>UDP で OSC を送る。1 パケット 1 メッセージ。</summary>
public sealed class OscSender : IDisposable
{
    readonly UdpClient _udp = new(AddressFamily.InterNetwork);

    public IPEndPoint Target { get; set; } = new(IPAddress.Loopback, 9000);

    public OscSender() { }
    public OscSender(IPEndPoint target) => Target = target;

    /// <summary>ホスト名または IP とポートから送信先を作る。</summary>
    public static IPEndPoint ResolveTarget(string host, int port)
    {
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "ポートは 1〜65535 です。");
        if (IPAddress.TryParse(host?.Trim(), out var ip)) return new IPEndPoint(ip, port);
        var addrs = Dns.GetHostAddresses(host?.Trim() ?? "");
        var v4 = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new ArgumentException("送信先の IP を解決できません: " + host);
        return new IPEndPoint(v4, port);
    }

    public async Task SendAsync(IEnumerable<OscMessage> messages, CancellationToken ct = default)
    {
        var target = Target;
        foreach (var m in messages)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = OscCodec.Encode(m);
            await _udp.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
        }
    }

    public Task SendAsync(OscMessage message, CancellationToken ct = default) => SendAsync(new[] { message }, ct);

    public void Dispose() => _udp.Dispose();
}