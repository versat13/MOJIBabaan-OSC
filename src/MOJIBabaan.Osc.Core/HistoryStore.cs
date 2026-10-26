using System.Text.Encodings.Web;
using System.Text.Json;

namespace MOJIBabaan.Osc.Core;

public sealed class HistoryItem
{
    public string Line1 { get; set; } = "";
    public string Line2 { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTime Time { get; set; }

    public string Label => string.Join(" / ", new[] { Line1, Line2 }.Where(x => x.Length > 0));
}

/// <summary>入力履歴。新しい順。ピン留めは上限（50 件）に数えない。Path が null なら保存しない。</summary>
public sealed class HistoryStore
{
    public const int MaxRecent = 50;

    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string? Path { get; }
    public List<HistoryItem> Items { get; private set; } = new();

    public HistoryStore(string? path) => Path = path;

    public static string DefaultPath => System.IO.Path.Combine(AppDataDir, "history.json");

    public static string AppDataDir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MOJIBabaan-OSC");

    public IEnumerable<HistoryItem> Pinned => Items.Where(x => x.Pinned);
    public IEnumerable<HistoryItem> Recent => Items.Where(x => !x.Pinned);

    /// <summary>読み込む。壊れていれば .bak へ退避して空で始める。</summary>
    public void Load()
    {
        Items = new List<HistoryItem>();
        if (Path == null || !File.Exists(Path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(Path), s_json);
            if (list != null) Items = list.Where(x => x != null && (x.Line1.Length > 0 || x.Line2.Length > 0)).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Move(Path, Path + ".bak", true); } catch { }
            Items = new List<HistoryItem>();
        }
        Trim();
    }

    public void Save()
    {
        if (Path == null) return;
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Items, s_json), new System.Text.UTF8Encoding(false));
        File.Move(tmp, Path, true);
    }

    /// <summary>先頭に追加する。同じ文言があれば先頭へ移す（ピンは保つ）。両方空なら何もしない。</summary>
    public HistoryItem? Add(string line1, string line2, DateTime? now = null)
    {
        line1 ??= ""; line2 ??= "";
        if (line1.Length == 0 && line2.Length == 0) return null;

        var item = Items.FirstOrDefault(x => x.Line1 == line1 && x.Line2 == line2);
        if (item != null) Items.Remove(item);
        else item = new HistoryItem { Line1 = line1, Line2 = line2 };
        item.Time = now ?? DateTime.Now;
        Items.Insert(0, item);
        Trim();
        Save();
        return item;
    }

    public void SetPinned(HistoryItem item, bool pinned)
    {
        item.Pinned = pinned;
        Trim();
        Save();
    }

    public void Remove(HistoryItem item)
    {
        Items.Remove(item);
        Save();
    }

    // ピン留め以外が 50 件を超えたら古いものから消す
    void Trim()
    {
        int recent = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Pinned) continue;
            if (++recent > MaxRecent) { Items.RemoveAt(i); i--; }
        }
    }
}