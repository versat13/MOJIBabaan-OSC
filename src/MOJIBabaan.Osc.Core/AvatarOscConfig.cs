namespace MOJIBabaan.Osc.Core;

/// <summary>
/// VRChat の OSC 設定ファイル（usr_*\Avatars\avtr_*.json）で、アバターが BBN パラメータを持つか調べる。
/// OSCQuery が使えないときの代替。
/// </summary>
public static class AvatarOscConfig
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "VRChat", "VRChat", "OSC");

    /// <summary>true=持つ、false=持たない、null=設定ファイルが見つからない。</summary>
    public static bool? HasBbn(string avatarId, string? root = null)
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
                if (File.ReadAllText(file).Contains("BBN/O00h", StringComparison.Ordinal)) return true;
            }
            catch (IOException) { found = false; }
        }
        return found ? false : null;
    }
}