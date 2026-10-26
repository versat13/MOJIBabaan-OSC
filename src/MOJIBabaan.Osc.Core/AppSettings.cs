using System.Text.Encodings.Web;
using System.Text.Json;

namespace MOJIBabaan.Osc.Core;

/// <summary>設定。%AppData%\MOJIBabaan-OSC\settings.json に保存する。</summary>
public sealed class AppSettings
{
    public string SendHost { get; set; } = "127.0.0.1";
    public int SendPort { get; set; } = 9000;
    public int ReceivePort { get; set; } = 9001;
    /// <summary>OSCQuery を使う（受信ポートの広告、VRChat の送信先と現在値の取得）。</summary>
    public bool UseOscQuery { get; set; } = true;

    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultPath => Path.Combine(HistoryStore.AppDataDir, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), s_json) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, s_json), new System.Text.UTF8Encoding(false));
    }
}