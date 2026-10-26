namespace MOJIBabaan.Osc.Core;

/// <summary>登録・再生の手順。待ち時間は差し替えられる（テスト用）。</summary>
public sealed class BabaanSender
{
    public static readonly TimeSpan ReqPulse = TimeSpan.FromMilliseconds(300);

    readonly OscSender _sender;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public BabaanSender(OscSender sender, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _sender = sender;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    /// <summary>O と OL を全部送る（32 セルなら 66 個、20 セルなら 42 個）。</summary>
    public Task RegisterAsync(BabaanText.Result r, CancellationToken ct = default)
        => _sender.SendAsync(BabaanMessages.BuildRegister(r), ct);

    /// <summary>Req=17 を送り、0.3 秒後に 0 へ戻す。途中で中断しても 0 は必ず送る。</summary>
    public async Task PlayAsync(CancellationToken ct = default)
    {
        await _sender.SendAsync(BabaanMessages.Req(BabaanMessages.ReqPlay), ct).ConfigureAwait(false);
        try { await _delay(ReqPulse, ct).ConfigureAwait(false); }
        finally { await _sender.SendAsync(BabaanMessages.Req(0)).ConfigureAwait(false); }
    }

    /// <summary>全員に届くまで 1 秒ずつ待つ（残り秒数を通知。最後に 0）。待ち時間は <see cref="SyncTiming.Total"/> を切り上げた秒数。</summary>
    public async Task WaitSyncAsync(int cells, Action<int>? countdown = null, CancellationToken ct = default)
    {
        int secs = (int)Math.Ceiling(SyncTiming.Total(cells).TotalSeconds);
        for (int s = secs; s >= 1; s--)
        {
            countdown?.Invoke(s);
            await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
        countdown?.Invoke(0);
    }

    /// <summary>登録 → 全員に届くまで待つ → 再生。</summary>
    public async Task RegisterAndPlayAsync(BabaanText.Result r, Action<int>? countdown = null, CancellationToken ct = default)
    {
        await RegisterAsync(r, ct).ConfigureAwait(false);
        await WaitSyncAsync(r.Cells, countdown, ct).ConfigureAwait(false);
        await PlayAsync(ct).ConfigureAwait(false);
    }
}