namespace MOJIBabaan.Osc.Core;

/// <summary>OSC メッセージ 1 つ。引数は int / float / string / bool。</summary>
public sealed record OscMessage(string Address, params object[] Args)
{
    public static OscMessage Int(string address, int value) => new(address, value);
    public static OscMessage Bool(string address, bool value) => new(address, value);

    public override string ToString() => Address + " " + string.Join(" ", Args.Select(a => a?.ToString() ?? "null"));
}

public sealed class OscFormatException : Exception
{
    public OscFormatException(string message) : base(message) { }
}