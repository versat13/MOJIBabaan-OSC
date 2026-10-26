using System.Net;
using System.Net.Sockets;

namespace MOJIBabaan.Osc.Core;

/// <summary>UDP で OSC を待ち受ける。受信したメッセージはスレッドプール上で通知する。</summary>
public sealed class OscReceiver : IDisposable
{
    UdpClient? _udp;
    CancellationTokenSource? _cts;
    Task? _loop;

    /// <summary>実際に待ち受けているポート（Start に 0 を渡したときの割り当て確認用）。</summary>
    public int Port { get; private set; }
    public bool IsRunning => _udp != null;

    public event Action<OscMessage>? MessageReceived;
    /// <summary>壊れたパケットを受けたとき。</summary>
    public event Action<Exception>? PacketError;

    /// <summary>待ち受けを始める。ポートが使えなければ SocketException。</summary>
    public void Start(int port, IPAddress? bindAddress = null)
    {
        Stop();
        var udp = new UdpClient(System.Net.Sockets.AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
        try { udp.Client.Bind(new IPEndPoint(bindAddress ?? IPAddress.Loopback, port)); }
        catch { udp.Dispose(); throw; }
        _udp = udp;
        Port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Loop(udp, _cts.Token));
    }

    async Task Loop(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (ct.IsCancellationRequested) break; continue; }

            List<OscMessage> msgs;
            try { msgs = OscCodec.Decode(r.Buffer); }
            catch (OscFormatException e) { PacketError?.Invoke(e); continue; }

            foreach (var m in msgs)
            {
                try { MessageReceived?.Invoke(m); } catch { /* 購読側の例外で受信を止めない */ }
            }
        }
    }

    public void Stop()
    {
        var cts = _cts; var udp = _udp;
        _cts = null; _udp = null; Port = 0;
        try { cts?.Cancel(); } catch { }
        try { udp?.Dispose(); } catch { }
        try { _loop?.Wait(500); } catch { }
        cts?.Dispose();
        _loop = null;
    }

    public void Dispose() => Stop();
}