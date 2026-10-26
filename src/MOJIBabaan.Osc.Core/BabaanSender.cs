namespace MOJIBabaan.Osc.Core;

/// <summary>登録・再生の手順。待ち時間は差し替えられる（テスト用）。</summary>
public sealed class BabaanSender
{
    public static readonly TimeSpan SyncWait = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan ReqPulse = TimeSpan.FromMilliseconds(300);

    readonly OscSender _sender;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public BabaanSender(OscSender sender, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _sender = sender;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    /// <summary>O と OL の 42 個を送る。</summary>
    public Task RegisterAsync(BabaanText.Result r, CancellationToken ct = default)
        => _sender.SendAsync(BabaanMessages.BuildRegister(r), ct);

    /// <summary>Req=17 を送り、0.3 秒後に 0 へ戻す。途中で中断しても 0 は必ず送る。</summary>
    public async Task PlayAsync(CancellationToken ct = default)
    {
        await _sender.SendAsync(BabaanMessages.Req(BabaanMessages.ReqPlay), ct).ConfigureAwait(false);
        try { await _delay(ReqPulse, ct).ConfigureAwait(false); }
        finally { await _sender.SendAsync(BabaanMessages.Req(0)).ConfigureAwait(false); }
    }

    /// <summary>登録 → 6 秒待つ（残り秒数を通知）→ 再生。</summary>
    public async Task RegisterAndPlayAsync(BabaanText.Result r, Action<int>? countdown = null, CancellationToken ct = default)
    {
        await RegisterAsync(r, ct).ConfigureAwait(false);
        int secs = (int)SyncWait.TotalSeconds;
        for (int s = secs; s >= 1; s--)
        {
            countdown?.Invoke(s);
            await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
        countdown?.Invoke(0);
        await PlayAsync(ct).ConfigureAwait(false);
    }
}