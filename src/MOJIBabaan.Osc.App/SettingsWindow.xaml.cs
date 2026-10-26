using System.Windows;
using MOJIBabaan.Osc.Core;
using Wpf.Ui.Controls;

namespace MOJIBabaan.Osc.Gui;

public partial class SettingsWindow : FluentWindow
{
    readonly AppSettings _s;

    public SettingsWindow(AppSettings s)
    {
        _s = s;
        InitializeComponent();
        ErrorText.Foreground = Palette.Red;
        HostBox.Text = s.SendHost;
        SendPortBox.Text = s.SendPort.ToString();
        RecvPortBox.Text = s.ReceivePort.ToString();
        QuerySwitch.IsChecked = s.UseOscQuery;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0) { ErrorText.Text = "IP アドレスを入力してください。"; return; }
        if (!int.TryParse(SendPortBox.Text.Trim(), out int sp) || sp < 1 || sp > 65535) { ErrorText.Text = "送信ポートは 1〜65535 の数字にしてください。"; return; }
        if (!int.TryParse(RecvPortBox.Text.Trim(), out int rp) || rp < 1 || rp > 65535) { ErrorText.Text = "受信ポートは 1〜65535 の数字にしてください。"; return; }
        try { OscSender.ResolveTarget(host, sp); }
        catch (ArgumentException) { ErrorText.Text = "その IP アドレスは使えません。"; return; }

        _s.SendHost = host; _s.SendPort = sp; _s.ReceivePort = rp;
        _s.UseOscQuery = QuerySwitch.IsChecked == true;
        DialogResult = true;
    }
}