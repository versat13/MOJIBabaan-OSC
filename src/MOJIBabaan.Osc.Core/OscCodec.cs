using System.Buffers.Binary;
using System.Text;

namespace MOJIBabaan.Osc.Core;

/// <summary>OSC 1.0 のエンコード・デコード（Int / Float / String / True / False のみ）。</summary>
public static class OscCodec
{
    public static byte[] Encode(OscMessage m)
    {
        if (string.IsNullOrEmpty(m.Address) || m.Address[0] != '/')
            throw new ArgumentException("アドレスは / で始めてください。", nameof(m));

        using var ms = new MemoryStream();
        WriteString(ms, m.Address);

        var tags = new StringBuilder(",");
        foreach (var a in m.Args)
        {
            switch (a)
            {
                case int: tags.Append('i'); break;
                case float: tags.Append('f'); break;
                case string: tags.Append('s'); break;
                case bool b: tags.Append(b ? 'T' : 'F'); break;
                default: throw new ArgumentException("未対応の引数型です: " + (a?.GetType().Name ?? "null"), nameof(m));
            }
        }
        WriteString(ms, tags.ToString());

        Span<byte> buf = stackalloc byte[4];
        foreach (var a in m.Args)
        {
            switch (a)
            {
                case int i:
                    BinaryPrimitives.WriteInt32BigEndian(buf, i);
                    ms.Write(buf);
                    break;
                case float f:
                    BinaryPrimitives.WriteSingleBigEndian(buf, f);
                    ms.Write(buf);
                    break;
                case string s:
                    WriteString(ms, s);
                    break;
            }
        }
        return ms.ToArray();
    }

    // 文字列 + 終端の 0 を 4 バイト境界まで 0 で詰める
    static void WriteString(MemoryStream ms, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        ms.Write(bytes);
        int pad = 4 - (bytes.Length % 4);   // 終端の 0 を含め 1〜4 個
        for (int i = 0; i < pad; i++) ms.WriteByte(0);
    }

    /// <summary>パケット（メッセージまたはバンドル）を展開する。壊れていれば OscFormatException。</summary>
    public static List<OscMessage> Decode(ReadOnlySpan<byte> data)
    {
        var list = new List<OscMessage>();
        DecodeInto(data, list, 0);
        return list;
    }

    static void DecodeInto(ReadOnlySpan<byte> data, List<OscMessage> list, int depth)
    {
        if (depth > 8) throw new OscFormatException("バンドルの入れ子が深すぎます。");
        if (data.Length == 0) throw new OscFormatException("空のパケットです。");

        if (data[0] == '#')
        {
            if (data.Length < 16 || !data[..8].SequenceEqual("#bundle\0"u8))
                throw new OscFormatException("バンドルの先頭が不正です。");
            int pos = 16;                                    // "#bundle\0" と timetag
            while (pos < data.Length)
            {
                if (pos + 4 > data.Length) throw new OscFormatException("バンドルの要素長が足りません。");
                int size = BinaryPrimitives.ReadInt32BigEndian(data.Slice(pos, 4));
                pos += 4;
                if (size < 0 || pos + size > data.Length) throw new OscFormatException("バンドルの要素長が不正です。");
                DecodeInto(data.Slice(pos, size), list, depth + 1);
                pos += size;
            }
            return;
        }

        int p = 0;
        string address = ReadString(data, ref p);
        if (address.Length == 0 || address[0] != '/') throw new OscFormatException("アドレスが不正です。");
        if (p >= data.Length) { list.Add(new OscMessage(address)); return; }   // 型タグ無しの古い形式

        string tags = ReadString(data, ref p);
        if (tags.Length == 0 || tags[0] != ',') throw new OscFormatException("型タグが不正です。");

        var args = new List<object>();
        for (int t = 1; t < tags.Length; t++)
        {
            switch (tags[t])
            {
                case 'i': args.Add(BinaryPrimitives.ReadInt32BigEndian(Take(data, ref p, 4))); break;
                case 'f': args.Add(BinaryPrimitives.ReadSingleBigEndian(Take(data, ref p, 4))); break;
                case 's': args.Add(ReadString(data, ref p)); break;
                case 'T': args.Add(true); break;
                case 'F': args.Add(false); break;
                case 'N': case 'I': break;                                          // 値なし
                case 'h': args.Add(BinaryPrimitives.ReadInt64BigEndian(Take(data, ref p, 8))); break;
                case 'd': args.Add(BinaryPrimitives.ReadDoubleBigEndian(Take(data, ref p, 8))); break;
                case 't': Take(data, ref p, 8); break;
                case 'c': case 'r': case 'm': Take(data, ref p, 4); break;
                case 'b':
                    {
                        int len = BinaryPrimitives.ReadInt32BigEndian(Take(data, ref p, 4));
                        if (len < 0) throw new OscFormatException("blob の長さが不正です。");
                        Take(data, ref p, (len + 3) & ~3);
                        break;
                    }
                default: throw new OscFormatException("未対応の型タグです: " + tags[t]);
            }
        }
        list.Add(new OscMessage(address, args.ToArray()));
    }

    static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int p, int n)
    {
        if (n < 0 || p + n > data.Length) throw new OscFormatException("データが足りません。");
        var s = data.Slice(p, n);
        p += n;
        return s;
    }

    static string ReadString(ReadOnlySpan<byte> data, ref int p)
    {
        int end = data.Slice(p).IndexOf((byte)0);
        if (end < 0) throw new OscFormatException("文字列の終端がありません。");
        var s = Encoding.UTF8.GetString(data.Slice(p, end));
        int total = (end + 4) & ~3;                          // 終端の 0 込みで 4 の倍数
        if (p + total > data.Length) throw new OscFormatException("文字列の詰め物が足りません。");
        p += total;
        return s;
    }
}