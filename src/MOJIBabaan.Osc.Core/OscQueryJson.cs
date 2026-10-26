using System.Text.Json;

namespace MOJIBabaan.Osc.Core;

/// <summary>OSCQuery（VRChat の HTTP 応答）の JSON から BBN の値を取り出す。</summary>
public static class OscQueryJson
{
    /// <summary>Names は見つかったキー（"O00h" など。値の有無によらない）。</summary>
    public sealed record BbnQuery(Dictionary<string, int> Values, bool HasBbn, HashSet<string>? Names = null)
    {
        /// <summary>見つかったキーから分かる容量（1 行の字数）。分からなければ null。</summary>
        public int? PerLine => Names == null ? null : AvatarCapacity.FromNames(Names);
    }

    /// <summary>/avatar/parameters/BBN 以下の応答。キーは "O00h"、"OL1" など。</summary>
    public static BbnQuery ParseBbn(string json)
    {
        var values = new Dictionary<string, int>();
        var names = new HashSet<string>();
        bool has = false;
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement, values, names, ref has, 0);
        return new BbnQuery(values, has, names);
    }

    static void Walk(JsonElement node, Dictionary<string, int> values, HashSet<string> names, ref bool has, int depth)
    {
        if (depth > 16 || node.ValueKind != JsonValueKind.Object) return;

        if (node.TryGetProperty("FULL_PATH", out var fp) && fp.ValueKind == JsonValueKind.String)
        {
            var path = fp.GetString() ?? "";
            if (path.StartsWith(BabaanMessages.BbnPrefix, StringComparison.Ordinal))
            {
                has = true;
                var key = path.Substring(BabaanMessages.BbnPrefix.Length);
                names.Add(key);
                if (node.TryGetProperty("VALUE", out var val) && val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0
                    && TryInt(val[0], out int v))
                    values[key] = v;
            }
        }

        if (node.TryGetProperty("CONTENTS", out var contents) && contents.ValueKind == JsonValueKind.Object)
            foreach (var p in contents.EnumerateObject())
                Walk(p.Value, values, names, ref has, depth + 1);
    }

    static bool TryInt(JsonElement e, out int v)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number: v = (int)Math.Round(e.GetDouble()); return true;
            case JsonValueKind.True: v = 1; return true;
            case JsonValueKind.False: v = 0; return true;
            default: v = 0; return false;
        }
    }

    /// <summary>/avatar/change の応答から現在のアバター ID を取り出す。</summary>
    public static string? ParseAvatarId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("VALUE", out var val)
            && val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0
            && val[0].ValueKind == JsonValueKind.String)
            return val[0].GetString();
        return null;
    }
}