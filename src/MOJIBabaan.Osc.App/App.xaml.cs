using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace MOJIBabaan.Osc.Gui;

public partial class App : Application
{
    public static readonly Color Gold = Color.FromRgb(0xF2, 0xC9, 0x4C);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        ApplicationAccentColorManager.Apply(Gold, ApplicationTheme.Dark);
        // 既定ではダークテーマで明るく寄るので、主ボタンの塗りは金そのものにする
        foreach (var k in new[] { "AccentFillColorDefault", "SystemAccentColor", "SystemAccentColorPrimary", "SystemAccentColorSecondary", "SystemAccentColorTertiary" })
        { Resources[k] = Gold; SetBrush(k + "Brush", Gold); }
        SetBrush("AccentFillColorDefaultBrush", Gold);
        SetBrush("AccentFillColorSecondaryBrush", Color.FromRgb(0xE3, 0xB8, 0x39));
        SetBrush("AccentFillColorTertiaryBrush", Color.FromRgb(0xC9, 0xA0, 0x2E));
        SetBrush("TextOnAccentFillColorPrimaryBrush", Color.FromRgb(0x1A, 0x14, 0x00));
        SetBrush("TextOnAccentFillColorSecondaryBrush", Color.FromRgb(0x1A, 0x14, 0x00));

        string? snapshot = null;
        for (int i = 0; i < e.Args.Length - 1; i++)
            if (e.Args[i] == "--snapshot") snapshot = e.Args[i + 1];

        var vm = new MainViewModel(demo: snapshot != null);
        var win = new MainWindow(vm);
        MainWindow = win;
        win.Show();

        if (snapshot != null)
        {
            win.ContentRendered += async (_, _) =>
            {
                await Task.Delay(1200);
                try
                {
                    SaveSnapshot(win, snapshot);
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    Shutdown(2);
                }
            };
        }
    }

    void SetBrush(string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        Resources[key] = b;
    }

    static void SaveSnapshot(Window win, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(win);
        int w = (int)Math.Ceiling(win.ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(win.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(win);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var fs = File.Create(full);
        enc.Save(fs);
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show("予期しないエラーが発生しました。\n\n" + e.Exception.Message, "MOJIBabaan OSC", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void LogError(Exception ex)
    {
        try
        {
            var dir = Core.HistoryStore.AppDataDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now.ToString("s") + "\n" + ex + "\n\n");
        }
        catch { }
    }
}