using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media;
using System.Windows.Threading;
using MOJIBabaan.Osc.Core;
using Wpf.Ui.Controls;

namespace MOJIBabaan.Osc.Gui;

public sealed class CellVM : ViewModelBase
{
    string _text = "";
    CellState _state;

    public string Text => _text;
    public Brush Fill => _state switch { CellState.Ok => Palette.GoldSoft, CellState.Unsupported => Palette.RedSoft, _ => Palette.Faint };
    public Brush Stroke => _state switch { CellState.Ok => Palette.GoldLine, CellState.Unsupported => Palette.RedLine, _ => Palette.FaintLine };
    public Brush Fore => _state switch { CellState.Unsupported => Palette.Red, _ => Palette.Gold };

    public void Update(string text, CellState state)
    {
        _text = text; _state = state;
        Raise(nameof(Text)); Raise(nameof(Fill)); Raise(nameof(Stroke)); Raise(nameof(Fore));
    }
}

public sealed class HistoryRow : ViewModelBase
{
    public HistoryItem Item { get; }
    public HistoryRow(HistoryItem item) => Item = item;
    public string Label => Item.Label;
    public bool Pinned => Item.Pinned;
}

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly bool _demo;
    readonly CharMap _map = CharMap.Default;
    readonly OscSender _sender = new();
    readonly BabaanSender _bbn;
    readonly OscReceiver _receiver = new();
    readonly AvatarState _state;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    VrcOscQuery? _query;
    long _lastRecvTicks;
    bool _cfgBbn;
    int _checkGen;

    public AppSettings Settings { get; private set; }
    public HistoryStore History { get; }
    public ObservableCollection<CellVM> Cells { get; } = new(Enumerable.Range(0, BabaanText.Cells).Select(_ => new CellVM()));
    public ObservableCollection<HistoryRow> Rows { get; } = new();

    public MainViewModel(bool demo)
    {
        _demo = demo;
        _state = new AvatarState(_map);
        _bbn = new BabaanSender(_sender);
        Settings = demo ? new AppSettings() : AppSettings.Load();
        History = new HistoryStore(demo ? null : HistoryStore.DefaultPath);

        if (demo) LoadDemo();
        else
        {
            History.Load();
            ApplySettings(startup: true);
            _receiver.MessageReceived += m => { Interlocked.Exchange(ref _lastRecvTicks, DateTime.UtcNow.Ticks); _ui.BeginInvoke(() => OnMessage(m)); };
            _timer.Tick += (_, _) => UpdateStatus();
            _timer.Start();
        }
        RebuildRows();
        UpdatePreview();
        UpdateStatus();
        RefreshRegistered();
    }

    // ---------- 入力とプレビュー ----------

    string _line1 = "", _line2 = "";
    public string Line1 { get => _line1; set { if (Set(ref _line1, value)) UpdatePreview(); } }
    public string Line2 { get => _line2; set { if (Set(ref _line2, value)) UpdatePreview(); } }

    BabaanText.Result _result = BabaanText.ParseLines("", "", CharMap.Default);

    string _count1 = "0/10", _count2 = "0/10";
    public string Count1 { get => _count1; private set => Set(ref _count1, value); }
    public string Count2 { get => _count2; private set => Set(ref _count2, value); }
    Brush _count1Brush = Palette.TextDim, _count2Brush = Palette.TextDim;
    public Brush Count1Brush { get => _count1Brush; private set => Set(ref _count1Brush, value); }
    public Brush Count2Brush { get => _count2Brush; private set => Set(ref _count2Brush, value); }

    string _unsupportedNote = "", _overflowNote = "";
    public string UnsupportedNote { get => _unsupportedNote; private set { if (Set(ref _unsupportedNote, value)) Raise(nameof(HasUnsupported)); } }
    public string OverflowNote { get => _overflowNote; private set { if (Set(ref _overflowNote, value)) Raise(nameof(HasOverflow)); } }
    public bool HasUnsupported => _unsupportedNote.Length > 0;
    public bool HasOverflow => _overflowNote.Length > 0;

    void UpdatePreview()
    {
        var r = BabaanText.ParseLines(_line1, _line2, _map);
        _result = r;
        for (int i = 0; i < BabaanText.Cells; i++) Cells[i].Update(r.Chars[i], r.States[i]);
        Count1 = $"{r.Len1}/{BabaanText.MaxLine}";
        Count2 = $"{r.Len2}/{BabaanText.MaxLine}";
        Count1Brush = r.Over1 ? Palette.Red : Palette.TextDim;
        Count2Brush = r.Over2 ? Palette.Red : Palette.TextDim;
        UnsupportedNote = r.Unsupported.Count > 0
            ? "赤いマスの字は文字表に無いため空白になります: " + string.Join(" ", r.Unsupported.Select(BabaanText.Printable))
            : "";
        var parts = new List<string>();
        if (r.Over1) parts.Add($"1 行目の「{r.Overflow1}」");
        if (r.Over2) parts.Add($"2 行目の「{r.Overflow2}」");
        OverflowNote = parts.Count > 0 ? "10 文字を超えた部分は送られません: " + string.Join("、", parts) : "";
    }

    // ---------- ボタン ----------

    bool _busy;
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(CanOperate)); } }
    public bool CanOperate => !_busy;

    string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }
    Brush _statusBrush = Palette.TextDim;
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    void SetStatus(string text, bool error = false)
    {
        Status = text;
        StatusBrush = error ? Palette.Red : Palette.TextDim;
    }

    public async Task RegisterAsync(bool play)
    {
        if (_busy) return;
        if (_demo) { SetStatus("見本表示です。"); return; }
        Busy = true;
        var r = _result;
        try
        {
            if (play)
            {
                SetStatus("登録を送信しています…");
                await _bbn.RegisterAndPlayAsync(r, s => _ui.BeginInvoke(() =>
                    SetStatus(s > 0 ? $"全員に届くまで待っています。再生まで あと {s} 秒" : "再生を送信しています…")));
                SetStatus("登録して再生しました。");
            }
            else
            {
                await _bbn.RegisterAsync(r);
                SetStatus(r.IsEmpty ? "空の文言を登録しました。" : "登録しました（42 個を送信）。全員に届くまで約 4〜6 秒かかります。");
            }
            if (!r.IsEmpty) { History.Add(_line1.Trim(), _line2.Trim()); RebuildRows(); }
            ScheduleCheck(1500);
        }
        catch (Exception e) when (e is SocketException or ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
            SetStatus("送信できませんでした: " + e.Message, error: true);
        }
        finally { Busy = false; }
    }

    public async Task PlayAsync()
    {
        if (_busy) return;
        if (_demo) { SetStatus("見本表示です。"); return; }
        Busy = true;
        try
        {
            await _bbn.PlayAsync();
            SetStatus("再生を送信しました。");
        }
        catch (Exception e) when (e is SocketException or ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
            SetStatus("送信できませんでした: " + e.Message, error: true);
        }
        finally { Busy = false; }
    }

    // ---------- 履歴 ----------

    public void RebuildRows()
    {
        Rows.Clear();
        foreach (var it in History.Pinned) Rows.Add(new HistoryRow(it));
        foreach (var it in History.Recent) Rows.Add(new HistoryRow(it));
    }

    public void UseHistory(HistoryRow row)
    {
        Line1 = row.Item.Line1;
        Line2 = row.Item.Line2;
    }

    public void TogglePin(HistoryRow row)
    {
        History.SetPinned(row.Item, !row.Item.Pinned);
        RebuildRows();
    }

    public void RemoveHistory(HistoryRow row)
    {
        History.Remove(row.Item);
        RebuildRows();
    }

    // ---------- 登録内容（アバターが持っている文言） ----------

    bool _regKnown;
    string _regLine1 = "", _regLine2 = "";
    public bool RegKnown { get => _regKnown; private set { if (Set(ref _regKnown, value)) Raise(nameof(RegUnknown)); } }
    public bool RegUnknown => !_regKnown;
    public string RegLine1 { get => _regLine1; private set => Set(ref _regLine1, value); }
    public string RegLine2 { get => _regLine2; private set => Set(ref _regLine2, value); }

    void RefreshRegistered()
    {
        var t = _state.Restored;
        RegKnown = t != null;
        if (t == null) { RegLine1 = RegLine2 = ""; return; }
        RegLine1 = t.IsEmpty ? "（空）" : t.Line1;
        RegLine2 = t.Line2;
    }

    // ---------- 状態表示 ----------

    string _recvText = "VRChat: 受信なし";
    Brush _recvBrush = Palette.Gray;
    string _avatarText = "対応アバター: 未検出";
    Brush _avatarBrush = Palette.Gray;
    string _netNote = "";
    public string RecvText { get => _recvText; private set => Set(ref _recvText, value); }
    public Brush RecvBrush { get => _recvBrush; private set => Set(ref _recvBrush, value); }
    public string AvatarText { get => _avatarText; private set => Set(ref _avatarText, value); }
    public Brush AvatarBrush { get => _avatarBrush; private set => Set(ref _avatarBrush, value); }
    /// <summary>接続まわりの補足（ポート変更、OSCQuery の可否など）。</summary>
    public string NetNote { get => _netNote; private set { if (Set(ref _netNote, value)) Raise(nameof(HasNetNote)); } }
    public bool HasNetNote => _netNote.Length > 0;

    bool _demoRecv, _demoAvatar;

    void UpdateStatus()
    {
        bool recv = _demo ? _demoRecv : (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRecvTicks)) < TimeSpan.FromSeconds(15).Ticks;
        RecvText = recv ? "VRChat: 受信あり" : "VRChat: 受信なし";
        RecvBrush = recv ? Palette.Green : Palette.Gray;
        bool av = _demo ? _demoAvatar : (_state.SeenBbn || _cfgBbn);
        AvatarText = av ? "対応アバター: 検出" : "対応アバター: 未検出";
        AvatarBrush = av ? Palette.Green : Palette.Gray;
    }

    // ---------- 受信 ----------

    void OnMessage(OscMessage m)
    {
        bool isChange = m.Address == "/avatar/change";
        if (isChange) _cfgBbn = false;
        if (_state.Apply(m))
        {
            RefreshRegistered();
            UpdateStatus();
        }
        if (isChange) { SetStatus("アバターが変わりました。"); ScheduleCheck(1500); ScheduleCheck(5000); }
    }

    // アバターが BBN を持つか、現在値はいくつかを、OSCQuery と設定ファイルで調べる
    void ScheduleCheck(int delayMs)
    {
        if (_demo) return;
        int gen = _checkGen;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            await CheckAvatarAsync(gen).ConfigureAwait(false);
        });
    }

    async Task CheckAvatarAsync(int gen)
    {
        var snap = _query != null ? await _query.FetchAsync().ConfigureAwait(false) : null;
        string? id = snap?.AvatarId;
        bool? cfg = null;
        var idForCfg = id ?? await _ui.InvokeAsync(() => _state.AvatarId);
        if (idForCfg != null) cfg = AvatarOscConfig.HasBbn(idForCfg);

        await _ui.InvokeAsync(() =>
        {
            if (gen != _checkGen) return;
            if (id != null && id != _state.AvatarId) { _state.Reset(id); _cfgBbn = false; }
            bool changed = false;
            if (snap?.Bbn != null) changed |= _state.ApplyValues(snap.Bbn.Values, snap.Bbn.HasBbn);
            if (cfg == true) _cfgBbn = true;
            if (changed) RefreshRegistered();
            UpdateStatus();
        });
    }

    // ---------- 設定とネットワーク ----------

    static bool IsLoopback(string host)
        => IPAddress.TryParse(host.Trim(), out var ip) ? IPAddress.IsLoopback(ip) : host.Trim().Equals("localhost", StringComparison.OrdinalIgnoreCase);

    public void ApplySettings(bool startup = false)
    {
        if (_demo) return;
        _checkGen++;
        var notes = new List<string>();

        _receiver.Stop();
        _query?.Dispose();
        _query = null;

        IPEndPoint target;
        try { target = OscSender.ResolveTarget(Settings.SendHost, Settings.SendPort); }
        catch (Exception e) when (e is ArgumentException)
        {
            target = new IPEndPoint(IPAddress.Loopback, 9000);
            notes.Add("送信先を解決できないため 127.0.0.1:9000 を使います。");
        }
        _sender.Target = target;

        var bind = IsLoopback(Settings.SendHost) ? IPAddress.Loopback : IPAddress.Any;
        try { _receiver.Start(Settings.ReceivePort, bind); }
        catch (SocketException)
        {
            if (Settings.UseOscQuery)
            {
                try
                {
                    _receiver.Start(0, bind);
                    notes.Add($"受信ポート {Settings.ReceivePort} は使用中のため {_receiver.Port} で待ち受けています（OSCQuery で VRChat に知らせます）。");
                }
                catch (SocketException e) { notes.Add("受信を始められません: " + e.Message); }
            }
            else notes.Add($"受信ポート {Settings.ReceivePort} は使用中です。設定で変えるか、OSCQuery を有効にしてください。");
        }

        if (Settings.UseOscQuery && _receiver.IsRunning)
        {
            try
            {
                var q = new VrcOscQuery();
                q.VrcChanged += () => _ui.BeginInvoke(OnVrcChanged);
                q.Start(_receiver.Port);
                _query = q;
            }
            catch (Exception e)
            {
                App.LogError(e);
                notes.Add("OSCQuery を使えませんでした。ポート直指定で動作します（" + e.GetType().Name + "）。");
            }
        }

        NetNote = string.Join(" ", notes);
        if (!startup) { SetStatus("設定を反映しました。"); }
        ScheduleCheck(500);
    }

    void OnVrcChanged()
    {
        var q = _query;
        if (q == null) return;
        // OSCQuery が VRChat の OSC ポートを見つけたら、そちらへ送る
        if (q.VrcOscEndpoint is { } ep) _sender.Target = ep;
        ScheduleCheck(300);
    }

    // ---------- 見本（スクリーンショット用） ----------

    void LoadDemo()
    {
        _demoRecv = true; _demoAvatar = true;
        Line1 = "5000兆円"; Line2 = "欲しい";
        var now = DateTime.Now;
        History.Items.Add(new HistoryItem { Line1 = "5000兆円", Line2 = "欲しい", Pinned = true, Time = now });
        History.Items.Add(new HistoryItem { Line1 = "お風呂が", Line2 = "沸きました", Pinned = true, Time = now });
        History.Items.Add(new HistoryItem { Line1 = "お嬢様", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "ふざけるな", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "ババァーン", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "祝", Line2 = "100 万再生", Time = now });
        foreach (var m in BabaanMessages.BuildRegister(BabaanText.ParseLines("5000兆円", "欲しい", _map))) _state.Apply(m);
        SetStatus("登録しました（42 個を送信）。全員に届くまで約 4〜6 秒かかります。");
    }

    public void Dispose()
    {
        _timer.Stop();
        _receiver.Dispose();
        _query?.Dispose();
        _sender.Dispose();
    }
}