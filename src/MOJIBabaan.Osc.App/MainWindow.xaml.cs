using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MOJIBabaan.Osc.Gui;

public partial class MainWindow : FluentWindow
{
    readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        UnsupportedText.Foreground = Palette.Red;
        OverflowText.Foreground = Palette.Red;
        Reg1.Foreground = Palette.Gold;
        Reg2.Foreground = Palette.Gold;
        Closed += (_, _) => _vm.Dispose();
        Loaded += (_, _) => Line1Box.Focus();
    }

    async void Register_Click(object sender, RoutedEventArgs e) => await _vm.RegisterAsync(play: false);
    async void RegisterPlay_Click(object sender, RoutedEventArgs e) => await _vm.RegisterAsync(play: true);
    async void Play_Click(object sender, RoutedEventArgs e) => await _vm.PlayAsync();

    async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // 日本語入力の変換確定の Enter は Key.ImeProcessed で届くので、ここには来ない
        e.Handled = true;
        await _vm.RegisterAsync(play: false);
    }

    void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HistoryRow row) _vm.UseHistory(row);
    }

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HistoryRow row) _vm.TogglePin(row);
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_vm.Settings) { Owner = this };
        if (w.ShowDialog() == true)
        {
            try { _vm.Settings.Save(); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            _vm.ApplySettings();
        }
    }
}