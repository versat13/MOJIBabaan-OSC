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

/// <summary>スクリーンショット用に見せる状態。</summary>
public enum DemoState { Idle, Dirty, Sending, Received }

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
    readonly DispatcherTimer _sendTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    VrcOscQuery? _query;
    long _lastRecvTicks;
    bool _cfgBbn;
    int? _cfgPerLine;
    int _checkGen;

    public AppSettings Settings { get; private set; }
    public HistoryStore History { get; }
    public ObservableCollection<CellVM> Cells { get; } = new();
    public ObservableCollection<HistoryRow> Rows { get; } = new();

    /// <summary>登録済みの内容が（受信で）入れ替わった。画面が光る。</summary>
    public event Action? RegisteredReplaced;

    public MainViewModel(bool demo, DemoState demoState = DemoState.Idle)
    {
        _demo = demo;
        _state = new AvatarState(_map);
        _bbn = new BabaanSender(_sender);
        Settings = demo ? new AppSettings() : AppSettings.Load();
        History = new HistoryStore(demo ? null : HistoryStore.DefaultPath);

        RebuildCells();
        if (demo) LoadDemo(demoState);
        else
        {
            History.Load();
            ApplySettings(startup: true);
            _receiver.MessageReceived += m => { Interlocked.Exchange(ref _lastRecvTicks, DateTime.UtcNow.Ticks); _ui.BeginInvoke(() => OnMessage(m)); };
            _timer.Tick += (_, _) => UpdateStatus();
            _timer.Start();
            _sendTimer.Tick += (_, _) => TickSending();
        }
        RebuildRows();
        UpdatePreview();
        UpdateStatus();
        RefreshRegistered(announce: false);
    }

    // ---------- 容量（1 行の字数） ----------

    int _perLine = BabaanText.FullPerLine;
    bool _capacityKnown;
    /// <summary>入力できる 1 行の字数。アバターの容量が分かればそれ、不明なら 16。</summary>
    public int PerLine { get => _perLine; private set { if (Set(ref _perLine, value)) { Raise(nameof(CapacityText)); } } }
    public bool CapacityKnown { get => _capacityKnown; private set { if (Set(ref _capacityKnown, value)) Raise(nameof(CapacityText)); } }
    public Brush CapacityBrush => _capacityKnown ? Palette.Green : Palette.Amber;
    public string CapacityText => _capacityKnown ? $"容量: {_perLine} 字 × 2 行" : $"容量: 不明（{_perLine} 字で入力）";

    void RebuildCells()
    {
        int n = _perLine * BabaanText.MaxLines;
        while (Cells.Count > n) Cells.RemoveAt(Cells.Count - 1);
        while (Cells.Count < n) Cells.Add(new CellVM());
    }

    // アバターの容量を、受信・OSCQuery・設定ファイルの順に決める
    void UpdateCapacity()
    {
        int? known = _state.PerLine ?? _cfgPerLine;
        CapacityKnown = known != null;
        Raise(nameof(CapacityBrush));
        int pl = known ?? BabaanText.FullPerLine;
        if (pl == _perLine) return;
        PerLine = pl;
        RebuildCells();
        UpdatePreview();
    }

    // ---------- 入力とプレビュー ----------

    string _line1 = "", _line2 = "";
    public string Line1 { get => _line1; set { if (Set(ref _line1, value)) UpdatePreview(); } }
    public string Line2 { get => _line2; set { if (Set(ref _line2, value)) UpdatePreview(); } }

    BabaanText.Result _result = BabaanText.ParseLines("", "", CharMap.Default);

    string _count1 = "0/16", _count2 = "0/16";
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
        var r = BabaanText.ParseLines(_line1, _line2, _map, _perLine);
        _result = r;
        RebuildCells();
        for (int i = 0; i < r.Cells; i++) Cells[i].Update(r.Chars[i], r.States[i]);
        Count1 = $"{r.Len1}/{_perLine}";
        Count2 = $"{r.Len2}/{_perLine}";
        Count1Brush = r.Over1 ? Palette.Red : Palette.TextDim;
        Count2Brush = r.Over2 ? Palette.Red : Palette.TextDim;
        UnsupportedNote = r.Unsupported.Count > 0
            ? "赤いマスは空白になります: " + string.Join(" ", r.Unsupported.Select(BabaanText.Printable))
            : "";
        var parts = new List<string>();
        if (r.Over1) parts.Add($"「{r.Overflow1}」");
        if (r.Over2) parts.Add($"「{r.Overflow2}」");
        OverflowNote = parts.Count > 0 ? "超えた分は送られません: " + string.Join(" ", parts) : "";
        UpdateDirty();
    }

    // 入力と、アバターが持っている内容が違うあいだは true（「未登録」バッジと、矢印の点滅）
    bool _dirty;
    public bool IsDirty { get => _dirty; private set => Set(ref _dirty, value); }

    void UpdateDirty()
    {
        var t = _state.Restored;
        IsDirty = t == null ? !_result.IsEmpty : !t.Matches(_result);
    }

    // ---------- 登録（配信中） ----------

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

    bool _sending;
    string _sendLine1 = "", _sendLine2 = "", _sendRemaining = "";
    double _sendProgress;
    DateTime _sendStart;
    TimeSpan _sendTotal;
    public bool IsSending { get => _sending; private set { if (Set(ref _sending, value)) Raise(nameof(IsNotSending)); } }
    public bool IsNotSending => !_sending;
    public string SendLine1 { get => _sendLine1; private set => Set(ref _sendLine1, value); }
    public string SendLine2 { get => _sendLine2; private set => Set(ref _sendLine2, value); }
    /// <summary>全員に届くまでの進み具合（0〜1）。</summary>
    public double SendProgress { get => _sendProgress; private set => Set(ref _sendProgress, value); }
    public string SendRemaining { get => _sendRemaining; private set => Set(ref _sendRemaining, value); }

    // 入力の内容を「配信中」へ移して、全員に届くまでの進捗を始める
    void BeginSending(BabaanText.Result r)
    {
        SendLine1 = r.IsEmpty ? "（空）" : r.Line1Text;
        SendLine2 = r.Line2Text;
        _sendStart = DateTime.UtcNow;
        _sendTotal = SyncTiming.Total(r.Cells);
        SendProgress = 0;
        SendRemaining = RemainingText(_sendTotal);
        IsSending = true;
        if (!_demo) _sendTimer.Start();
    }

    static string RemainingText(TimeSpan left) => $"あと {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒";

    void TickSending()
    {
        var elapsed = DateTime.UtcNow - _sendStart;
        if (elapsed >= _sendTotal)
        {
            _sendTimer.Stop();
            IsSending = false;
            SendProgress = 0;
            ScheduleCheck(300);    // 届いたはずの内容を読み取り直す
            return;
        }
        SendProgress = elapsed.TotalSeconds / _sendTotal.TotalSeconds;
        SendRemaining = RemainingText(_sendTotal - elapsed);
    }

    public async Task RegisterAsync(bool play)
    {
        if (_busy) return;
        if (_demo) { SetStatus("見本表示です。"); return; }
        Busy = true;
        var r = _result;
        try
        {
            await _bbn.RegisterAsync(r);
            BeginSending(r);
            if (!r.IsEmpty) { History.Add(_line1.Trim(), _line2.Trim()); RebuildRows(); }
            ScheduleCheck(1500);

            if (play)
            {
                await _bbn.WaitSyncAsync(r.Cells, s => _ui.BeginInvoke(() =>
                    SetStatus(s > 0 ? $"再生まで あと {s} 秒" : "再生を送信しています…")));
                await _bbn.PlayAsync();
            }
            FinishStatus(play, r);
        }
        catch (Exception e) when (e is SocketException or ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
            SetStatus("送信できませんでした: " + e.Message, error: true);
        }
        finally { Busy = false; }
    }

    void FinishStatus(bool play, BabaanText.Result r)
    {
        string done = play ? "登録して再生しました。" : r.IsEmpty ? "空の文言を登録しました。" : "登録しました。";
        if (_capacityKnown) { SetStatus(done); return; }
        // 容量を判定できていない。10 字/行のアバターだと 11 字目以降は欠ける
        SetStatus(done + $" アバターの容量を判定できないため {_perLine} 字/行で送りました。10 字/行のアバターでは後半が欠けます。", error: true);
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

    /// <summary>確認なしで消す。ピン留め中でも消せる。</summary>
    public void RemoveHistory(HistoryRow row)
    {
        History.Remove(row.Item);
        RebuildRows();
    }

    // ---------- 登録済み（アバターから読み取った実物） ----------

    bool _regKnown;
    string _regLine1 = "", _regLine2 = "";
    string? _regKey;
    public bool RegKnown { get => _regKnown; private set { if (Set(ref _regKnown, value)) Raise(nameof(RegUnknown)); } }
    public bool RegUnknown => !_regKnown;
    public string RegLine1 { get => _regLine1; private set => Set(ref _regLine1, value); }
    public string RegLine2 { get => _regLine2; private set => Set(ref _regLine2, value); }
    bool _regEmpty;
    public bool RegEmpty { get => _regEmpty; private set { if (Set(ref _regEmpty, value)) { Raise(nameof(RegHasText)); Raise(nameof(RegShowEmpty)); } } }
    public bool RegHasText => _regKnown && !_regEmpty;
    public bool RegShowEmpty => _regKnown && _regEmpty;

    void RefreshRegistered(bool announce = true)
    {
        UpdateCapacity();
        var t = _state.Restored;
        RegKnown = t != null;
        Raise(nameof(RegHasText)); Raise(nameof(RegShowEmpty));
        string? key = t == null ? null : string.Join(",", t.Nos) + "/" + t.PerLine;
        if (t == null) { RegLine1 = RegLine2 = ""; RegEmpty = false; }
        else { RegLine1 = t.Line1; RegLine2 = t.Line2; RegEmpty = t.IsEmpty; }
        if (key != _regKey)
        {
            _regKey = key;
            if (announce && key != null) RegisteredReplaced?.Invoke();
        }
        UpdateDirty();
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
        if (isChange) { _cfgBbn = false; _cfgPerLine = null; }
        if (_state.Apply(m))
        {
            RefreshRegistered();
            UpdateStatus();
        }
        if (isChange) { SetStatus("アバターが変わりました。"); ScheduleCheck(1500); ScheduleCheck(5000); }
    }

    // アバターが BBN を持つか、容量と現在値はいくつかを、OSCQuery と設定ファイルで調べる
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
        AvatarOscConfig.Info? cfg = null;
        var idForCfg = id ?? await _ui.InvokeAsync(() => _state.AvatarId);
        if (idForCfg != null) cfg = AvatarOscConfig.Inspect(idForCfg);

        await _ui.InvokeAsync(() =>
        {
            if (gen != _checkGen) return;
            if (id != null && id != _state.AvatarId) { _state.Reset(id); _cfgBbn = false; _cfgPerLine = null; }
            if (snap?.Bbn != null)
            {
                _state.SetCapacity(snap.Bbn.PerLine);
                _state.ApplyValues(snap.Bbn.Values, snap.Bbn.HasBbn);
            }
            if (cfg?.HasBbn == true) { _cfgBbn = true; _cfgPerLine = cfg.PerLine; }
            RefreshRegistered();
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

    void Feed(string l1, string l2)
    {
        foreach (var m in BabaanMessages.BuildRegister(BabaanText.ParseLines(l1, l2, _map, BabaanText.FullPerLine))) _state.Apply(m);
    }

    void LoadDemo(DemoState s)
    {
        _demoRecv = true; _demoAvatar = true;
        var now = DateTime.Now;
        History.Items.Add(new HistoryItem { Line1 = "5000兆円", Line2 = "欲しい", Pinned = true, Time = now });
        History.Items.Add(new HistoryItem { Line1 = "お風呂が", Line2 = "沸きました", Pinned = true, Time = now });
        History.Items.Add(new HistoryItem { Line1 = "お嬢様", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "ふざけるな", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "ババァーン", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "祝", Line2 = "100 万再生", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "ごめんなさい", Line2 = "許してください", Time = now });
        History.Items.Add(new HistoryItem { Line1 = "ありがとう", Line2 = "ございました", Time = now });
        UpdateCapacity();

        switch (s)
        {
            case DemoState.Idle:        // 入力と登録済みが同じ
                Feed("5000兆円", "欲しい");
                Line1 = "5000兆円"; Line2 = "欲しい";
                break;
            case DemoState.Dirty:       // 入力を書き換えて、まだ登録していない
                Feed("5000兆円", "欲しい");
                Line1 = "お風呂が沸きました"; Line2 = "いますぐ入浴を";
                break;
            case DemoState.Sending:     // 登録を押して、全員に届くのを待っている
                Feed("5000兆円", "欲しい");
                Line1 = "お風呂が沸きました"; Line2 = "いますぐ入浴を";
                BeginSending(_result);
                SendProgress = 0.45;
                SendRemaining = RemainingText(_sendTotal * 0.55);
                SetStatus("登録しました。");
                break;
            case DemoState.Received:    // アバターの内容が入れ替わった直後
                Feed("お風呂が沸きました", "いますぐ入浴を");
                Line1 = "お風呂が沸きました"; Line2 = "いますぐ入浴を";
                break;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _sendTimer.Stop();
        _receiver.Dispose();
        _query?.Dispose();
        _sender.Dispose();
    }
}