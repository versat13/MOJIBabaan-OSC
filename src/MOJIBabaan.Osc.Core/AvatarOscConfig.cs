namespace MOJIBabaan.Osc.Core;

/// <summary>
/// VRChat の OSC 設定ファイル（usr_*\Avatars\avtr_*.json）で、アバターが BBN パラメータを持つか、容量はいくつかを調べる。
/// OSCQuery が使えないときの代替。
/// </summary>
public static class AvatarOscConfig
{
    /// <summary>設定ファイルから分かったこと。PerLine は容量（10 か 16）で、判定できなければ null。</summary>
    public sealed record Info(bool HasBbn, int? PerLine);

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "VRChat", "VRChat", "OSC");

    /// <summary>true=持つ、false=持たない、null=設定ファイルが見つからない。</summary>
    public static bool? HasBbn(string avatarId, string? root = null) => Inspect(avatarId, root)?.HasBbn;

    /// <summary>設定ファイルを調べる。ファイルが見つからなければ null。</summary>
    public static Info? Inspect(string avatarId, string? root = null)
    {
        if (string.IsNullOrWhiteSpace(avatarId) || avatarId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        root ??= DefaultRoot;
        if (!Directory.Exists(root)) return null;

        bool found = false;
        foreach (var user in Directory.EnumerateDirectories(root, "usr_*"))
        {
            var file = Path.Combine(user, "Avatars", avatarId + ".json");
            if (!File.Exists(file)) continue;
            try
            {
                found = true;
                var text = File.ReadAllText(file);
                if (text.Contains("BBN/O00h", StringComparison.Ordinal))
                    return new Info(true, AvatarCapacity.FromConfigText(text));
            }
            catch (IOException) { found = false; }
        }
        return found ? new Info(false, null) : null;
    }
}