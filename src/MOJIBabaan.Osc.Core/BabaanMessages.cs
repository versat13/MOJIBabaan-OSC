namespace MOJIBabaan.Osc.Core;

/// <summary>MOJIBabaan のアバターパラメータ名と、送るメッセージの組み立て。</summary>
public static class BabaanMessages
{
    public const string ParamPrefix = "/avatar/parameters/";
    public const string BbnPrefix = "/avatar/parameters/BBN/";
    public const int ReqPlay = 17;

    /// <summary>"BBN/O03h" のようなパラメータ名（/avatar/parameters/ の後ろ）。</summary>
    public static string CellParam(int cell, bool high) => $"BBN/O{cell:00}{(high ? 'h' : 'l')}";

    public static string Address(string param) => ParamPrefix + param;

    public static OscMessage IntParam(string param, int value) => new(Address(param), value);

    /// <summary>
    /// 登録用のメッセージ。O00h, O00l, ..., OL1, OL2 の順。
    /// 32 セルなら 66 個、旧アバター互換の 20 セルなら 42 個。
    /// </summary>
    public static List<OscMessage> BuildRegister(BabaanText.Result r)
    {
        var list = new List<OscMessage>(r.Cells * 2 + 2);
        for (int i = 0; i < r.Cells; i++)
        {
            int no = r.Nos[i];
            list.Add(IntParam(CellParam(i, true), no >> 8));
            list.Add(IntParam(CellParam(i, false), no & 0xFF));
        }
        list.Add(IntParam("BBN/OL1", r.SendLen1));
        list.Add(IntParam("BBN/OL2", r.SendLen2));
        return list;
    }

    public static OscMessage Req(int value) => IntParam("BBN/Req", value);
}