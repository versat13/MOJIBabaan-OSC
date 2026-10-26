namespace MOJIBabaan.Osc.Core;

/// <summary>アバターが持っている文言（O と OL から復元）。</summary>
public sealed record RestoredText(string Line1, string Line2, bool HasUnknown)
{
    public bool IsEmpty => Line1.Length == 0 && Line2.Length == 0;
    public string Label => string.Join(" ", new[] { Line1, Line2 }.Where(x => x.Length > 0));
}

/// <summary>
/// VRChat から受け取った BBN パラメータの現在値。UI スレッドからだけ触る前提（排他なし）。
/// </summary>
public sealed class AvatarState
{
    readonly CharMap _map;
    readonly int?[] _o = new int?[BabaanText.Cells * 2];   // [cell*2 + (0=h, 1=l)]
    int? _ol1, _ol2;

    public AvatarState(CharMap map) => _map = map;

    public string? AvatarId { get; private set; }
    /// <summary>BBN/ のパラメータを 1 つでも受け取った（または照会で見つけた）。</summary>
    public bool SeenBbn { get; private set; }

    public void Reset(string? avatarId)
    {
        AvatarId = avatarId;
        SeenBbn = false;
        Array.Clear(_o);
        _ol1 = _ol2 = null;
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
        else if (name.Length == 4 && name[0] == 'O'
                 && char.IsDigit(name[1]) && char.IsDigit(name[2])
                 && (name[3] == 'h' || name[3] == 'l'))
        {
            int cell = (name[1] - '0') * 10 + (name[2] - '0');
            if (cell < BabaanText.Cells)
            {
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

    /// <summary>40 個の O がそろっていれば文言を返す。そろっていなければ null（不明）。</summary>
    public RestoredText? Restored
    {
        get
        {
            if (_o.Any(x => x == null)) return null;
            bool unknown = false;
            string Line(int start, int? len)
            {
                var sb = new System.Text.StringBuilder();
                for (int c = 0; c < BabaanText.MaxLine; c++)
                {
                    int no = (_o[(start + c) * 2]!.Value << 8) + _o[(start + c) * 2 + 1]!.Value;
                    if (no == 0) { sb.Append(' '); continue; }
                    var ch = _map.CharOf(no);
                    if (ch == null) { sb.Append('?'); unknown = true; } else sb.Append(ch);
                }
                var s = sb.ToString();
                if (len is int n && n >= 0 && n < s.Length) s = s.Substring(0, n);
                return s.TrimEnd(' ');
            }
            return new RestoredText(Line(0, _ol1), Line(BabaanText.MaxLine, _ol2), unknown);
        }
    }
}