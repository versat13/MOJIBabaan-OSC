namespace MOJIBabaan.Osc.Core;

/// <summary>
/// 登録した内容が全員に届くまでの目安。
/// アバターのパラメータ同期は 0.1 秒に 1 個で、1 周は (セル数×2 + 2) 個。
/// 20 セルで 4.2 秒、32 セルで 6.6 秒。設置中かは分からないので 1.2 秒の余裕を足す。
/// </summary>
public static class SyncTiming
{
    public static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(1200);

    /// <summary>巡回 1 周ぶん。</summary>
    public static TimeSpan Sync(int cells) => TimeSpan.FromMilliseconds((cells * 2 + 2) * 100);

    /// <summary>余裕込み（20 セル 5.4 秒、32 セル 7.8 秒）。</summary>
    public static TimeSpan Total(int cells) => Sync(cells) + Margin;
}