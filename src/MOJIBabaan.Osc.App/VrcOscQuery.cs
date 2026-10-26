using System.Net;
using System.Net.Http;
using MOJIBabaan.Osc.Core;
using VRC.OSCQuery;

namespace MOJIBabaan.Osc.Gui;

/// <summary>
/// VRChat 公式の OSCQuery ライブラリの薄いラッパー。
/// 自分の受信ポートを広告し、VRChat の OSC ポートと OSCQuery（HTTP）を見つける。
/// </summary>
public sealed class VrcOscQuery : IDisposable
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    OSCQueryService? _service;
    volatile IPEndPoint? _vrcOsc;
    volatile IPEndPoint? _vrcHttp;

    /// <summary>VRChat が OSC を受け付けている UDP の宛先（見つかっていれば）。</summary>
    public IPEndPoint? VrcOscEndpoint => _vrcOsc;
    public bool VrcFound => _vrcHttp != null;

    /// <summary>VRChat の宛先が見つかった・変わったとき（スレッドプール上）。</summary>
    public event Action? VrcChanged;

    /// <summary>udpPort で待ち受けていることを広告して、VRChat の探索を始める。</summary>
    public void Start(int udpPort)
    {
        var tcp = VRC.OSCQuery.Extensions.GetAvailableTcpPort();
        _service = new OSCQueryServiceBuilder()
            .WithServiceName("MOJIBabaan-OSC")
            .WithHostIP(IPAddress.Loopback)
            .WithOscIP(IPAddress.Loopback)
            .WithTcpPort(tcp)
            .WithUdpPort(udpPort)
            .WithDiscovery(new MeaModDiscovery())
            .StartHttpServer()
            .AdvertiseOSC()
            .AdvertiseOSCQuery()
            .Build();

        _service.AddEndpoint<string>("/avatar/change", Attributes.AccessValues.WriteOnly);
        _service.OnOscQueryServiceAdded += OnQueryService;
        _service.OnOscServiceAdded += OnOscService;
        _service.RefreshServices();
        foreach (var p in _service.GetOSCQueryServices()) OnQueryService(p);
        foreach (var p in _service.GetOSCServices()) OnOscService(p);
    }

    static bool IsVrc(OSCQueryServiceProfile p) => p.name != null && p.name.StartsWith("VRChat-Client", StringComparison.OrdinalIgnoreCase);

    void OnQueryService(OSCQueryServiceProfile p)
    {
        if (!IsVrc(p)) return;
        _vrcHttp = new IPEndPoint(p.address, p.port);
        VrcChanged?.Invoke();
    }

    void OnOscService(OSCQueryServiceProfile p)
    {
        if (!IsVrc(p)) return;
        _vrcOsc = new IPEndPoint(p.address, p.port);
        VrcChanged?.Invoke();
    }

    public sealed record Snapshot(string? AvatarId, OscQueryJson.BbnQuery? Bbn);

    /// <summary>VRChat から現在のアバター ID と BBN の値を取る。VRChat に届かなければ null。</summary>
    public async Task<Snapshot?> FetchAsync()
    {
        var ep = _vrcHttp;
        if (ep == null) return null;
        var baseUrl = $"http://{ep.Address}:{ep.Port}";
        try
        {
            string? id = null;
            try { id = OscQueryJson.ParseAvatarId(await _http.GetStringAsync(baseUrl + "/avatar/change").ConfigureAwait(false)); }
            catch (HttpRequestException e) when (e.StatusCode != null) { }

            OscQueryJson.BbnQuery? bbn;
            try { bbn = OscQueryJson.ParseBbn(await _http.GetStringAsync(baseUrl + "/avatar/parameters/BBN").ConfigureAwait(false)); }
            catch (HttpRequestException e) when (e.StatusCode != null) { bbn = new OscQueryJson.BbnQuery(new Dictionary<string, int>(), false); }
            return new Snapshot(id, bbn);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try { _service?.Dispose(); } catch { }
        _http.Dispose();
    }
}