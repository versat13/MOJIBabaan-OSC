namespace MOJIBabaan.Osc.Core;

/// <summary>アバターが持っている文言（O と OL から復元）。</summary>
/// <param name="PerLine">1 行の字数（10 か 16）。</param>
/// <param name="Nos">セルごとの文字番号（PerLine × 2 個）。</param>
public sealed record RestoredText(string Line1, string Line2, bool HasUnknown, int PerLine, int[] Nos)
{
    public bool IsEmpty => Line1.Length == 0 && Line2.Length == 0;
    public string Label => string.Join(" ", new[] { Line1, Line2 }.Where(x => x.Length > 0));

    /// <summary>入力（セル化した結果）と同じ内容か。文字番号で比べる。</summary>
    public bool Matches(BabaanText.Result r) => r.PerLine == PerLine && r.Nos.AsSpan().SequenceEqual(Nos);
}

/// <summary>
/// VRChat から受け取った BBN パラメータの現在値。UI スレッドからだけ触る前提（排他なし）。
/// </summary>
public sealed class AvatarState
{
    readonly CharMap _map;
    readonly int?[] _o = new int?[BabaanText.MaxCells * 2];   // [cell*2 + (0=h, 1=l)]
    int? _ol1, _ol2;
    int? _capacity;       // OSCQuery などで分かった 1 行の字数
    bool _seenHigh;       // 20 番以降のセルを受け取った（= 32 セル）

    public AvatarState(CharMap map) => _map = map;

    public string? AvatarId { get; private set; }
    /// <summary>BBN/ のパラメータを 1 つでも受け取った（または照会で見つけた）。</summary>
    public bool SeenBbn { get; private set; }

    /// <summary>1 行の字数（10 か 16）。分からなければ null。20 番以降のセルを受け取ったら必ず 16。</summary>
    public int? PerLine => _seenHigh ? AvatarCapacity.Full : _capacity;

    public void Reset(string? avatarId)
    {
        AvatarId = avatarId;
        SeenBbn = false;
        Array.Clear(_o);
        _ol1 = _ol2 = null;
        _capacity = null;
        _seenHigh = false;
    }

    /// <summary>容量（1 行の字数）が分かったときに知らせる。変わったら true。null は何もしない。</summary>
    public bool SetCapacity(int? perLine)
    {
        if (perLine is not (AvatarCapacity.Legacy or AvatarCapacity.Full) || _capacity == perLine) return false;
        _capacity = perLine;
        return true;
    }

    /// <summary>OSC メッセージを反映する。状態が変わったら true。</summary>
    public bool Apply(OscMessage m)
    {
        if (m.Address == "/avatar/change")
        {
            if (m.Args.Length > 0 && m.Args[0] is string id) { Reset(id); return true; }
            return false;
        }
        if (!m.Address.StartsWith(BabaanMessages.BbnPrefix, StringComparison.Ordinal)) return false;
        var name = m.Address.Substring(BabaanMessages.BbnPrefix.Length);
        if (m.Args.Length == 0 || !TryToInt(m.Args[0], out int v)) { return MarkSeen(); }
        return ApplyValue(name, v);
    }

    /// <summary>OSCQuery で得た値（キーは "O00h" や "OL1"）を反映する。</summary>
    public bool ApplyValues(IReadOnlyDictionary<string, int> values, bool hasBbn)
    {
        bool changed = false;
        if (hasBbn) changed |= MarkSeen();
        foreach (var kv in values) changed |= ApplyValue(kv.Key, kv.Value);
        return changed;
    }

    bool MarkSeen()
    {
        if (SeenBbn) return false;
        SeenBbn = true;
        return true;
    }

    bool ApplyValue(string name, int v)
    {
        bool changed = MarkSeen();
        if (name == "OL1") { changed |= _ol1 != v; _ol1 = v; }
        else if (name == "OL2") { changed |= _ol2 != v; _ol2 = v; }
        else
        {
            int cell = AvatarCapacity.CellOfName(name);
            if (cell >= 0 && cell < BabaanText.MaxCells)
            {
                if (cell >= BabaanText.LegacyPerLine * 2 && !_seenHigh) { _seenHigh = true; changed = true; }
                int idx = cell * 2 + (name[3] == 'h' ? 0 : 1);
                changed |= _o[idx] != v;
                _o[idx] = v;
            }
        }
        return changed;
    }

    static bool TryToInt(object a, out int v)
    {
        switch (a)
        {
            case int i: v = i; return true;
            case float f: v = (int)Math.Round(f); return true;
            case bool b: v = b ? 1 : 0; return true;
            default: v = 0; return false;
        }
    }

    /// <summary>容量が分かっていて、そのぶんの O がそろっていれば文言を返す。そろっていなければ null（不明）。</summary>
    public RestoredText? Restored
    {
        get
        {
            if (PerLine is not int perLine) return null;
            int cells = perLine * BabaanText.MaxLines;
            for (int i = 0; i < cells * 2; i++) if (_o[i] == null) return null;
            bool unknown = false;
            var nos = new int[cells];
            for (int c = 0; c < cells; c++) nos[c] = (_o[c * 2]!.Value << 8) + _o[c * 2 + 1]!.Value;
            string Line(int start, int? len)
            {
                var sb = new System.Text.StringBuilder();
                for (int c = 0; c < perLine; c++)
                {
                    int no = nos[start + c];
                    if (no == 0) { sb.Append(' '); continue; }
                    var ch = _map.CharOf(no);
                    if (ch == null) { sb.Append('?'); unknown = true; } else sb.Append(ch);
                }
                var s = sb.ToString();
                if (len is int n && n >= 0 && n < s.Length) s = s.Substring(0, n);
                return s.TrimEnd(' ');
            }
            return new RestoredText(Line(0, _ol1), Line(perLine, _ol2), unknown, perLine, nos);
        }
    }
}