using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MOJIBabaan.Osc.Core;
using Wpf.Ui.Controls;

namespace MOJIBabaan.Osc.Gui;

public partial class MainWindow : FluentWindow
{
    /// <summary>この幅より狭いと、3 枚のカードを上から下へ並べる。</summary>
    const double VerticalBelow = 1100;

    readonly MainViewModel _vm;
    bool? _vertical;
    bool _wrapping, _composing;
    Storyboard? _flash;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        ApplyFlow(Width < VerticalBelow);
        SizeChanged += (_, _) => ApplyFlow(ActualWidth < VerticalBelow);
        Closed += (_, _) => _vm.Dispose();
        Loaded += (_, _) => Line1Box.Focus();
        _vm.RegisteredReplaced += () => PlayFlash();

        // 日本語入力の変換中は折り返さない（変換が壊れるため）。確定したら折り返す
        TextCompositionManager.AddPreviewTextInputUpdateHandler(Line1Box, (_, e) => _composing = !string.IsNullOrEmpty(e.TextComposition.CompositionText));
        Line1Box.PreviewTextInput += (_, _) => _composing = false;
        Line1Box.TextInput += (_, _) => { _composing = false; Dispatcher.BeginInvoke(TryWrap, System.Windows.Threading.DispatcherPriority.Background); };
        Line1Box.LostKeyboardFocus += (_, _) => { _composing = false; TryWrap(); };
    }

    // ---------- 入力 → 配信中 → 登録済み の並べ方 ----------

    void ApplyFlow(bool vertical)
    {
        if (_vertical == vertical) return;
        _vertical = vertical;
        var g = FlowGrid;
        g.ColumnDefinitions.Clear();
        g.RowDefinitions.Clear();
        UIElement[] items = { InputCard, ArrowA, SendCard, ArrowB, RegCard };
        if (vertical)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 5; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = i % 2 == 1 ? new GridLength(40) : GridLength.Auto });
                Grid.SetRow(items[i], i); Grid.SetColumn(items[i], 0);
            }
        }
        else
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            double[] stars = { 1.25, 0, 0.75, 0, 1.45 };
            double[] mins = { 470, 0, 200, 0, 300 };
            for (int i = 0; i < 5; i++)
            {
                g.ColumnDefinitions.Add(i % 2 == 1
                    ? new ColumnDefinition { Width = new GridLength(44) }
                    : new ColumnDefinition { Width = new GridLength(stars[i], GridUnitType.Star), MinWidth = mins[i] });
                Grid.SetRow(items[i], 0); Grid.SetColumn(items[i], i);
            }
        }
        ArrowAIcon.Symbol = ArrowBIcon.Symbol = vertical ? SymbolRegular.ArrowDown24 : SymbolRegular.ArrowRight24;
    }

    // ---------- 自動折り返し ----------

    void Line1_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_composing) TryWrap();
    }

    // 1 行目が上限を超えたら、超えた分を 2 行目の先頭へ送り、カーソルも 2 行目へ移す（貼り付けでも同じ）
    void TryWrap()
    {
        // XAML の読み込み中は、バインドの初期値で TextChanged が来る。2 行目の欄がまだ無いので何もしない
        if (_wrapping || _composing || !IsLoaded || Line1Box == null || Line2Box == null) return;
        var w = AutoWrap.Wrap(Line1Box.Text, Line2Box.Text, _vm.PerLine, Line1Box.CaretIndex);
        if (!w.Wrapped) return;
        _wrapping = true;
        try
        {
            Line1Box.Text = w.Line1;
            Line2Box.Text = w.Line2;
            if (w.CaretToLine2)
            {
                Line2Box.Focus();
                Line2Box.CaretIndex = w.Caret;
            }
            else Line1Box.CaretIndex = w.Caret;
        }
        finally { _wrapping = false; }
    }

    // ---------- 登録済みが入れ替わったとき光る ----------

    /// <summary>光るアニメーションを始める。atMs を渡すと、その時点で止める（スクリーンショット用）。</summary>
    public void PlayFlash(int? atMs = null)
    {
        _flash?.Stop(this);
        var sb = new Storyboard();

        var glow = new DoubleAnimationUsingKeyFrames();
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), new SineEase { EasingMode = EasingMode.EaseOut }));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000)), new SineEase { EasingMode = EasingMode.EaseIn }));
        Storyboard.SetTarget(glow, RegFlash);
        Storyboard.SetTargetProperty(glow, new PropertyPath(OpacityProperty));
        sb.Children.Add(glow);

        foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var pop = new DoubleAnimationUsingKeyFrames();
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), new SineEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            Storyboard.SetTarget(pop, RegScale);
            Storyboard.SetTargetProperty(pop, new PropertyPath(prop));
            sb.Children.Add(pop);
        }

        _flash = sb;
        sb.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        if (atMs is int ms)
        {
            sb.Pause(this);
            sb.Seek(this, TimeSpan.FromMilliseconds(ms), TimeSeekOrigin.BeginTime);
        }
    }

    // ---------- ボタンなど ----------

    async void Register_Click(object sender, RoutedEventArgs e) => await _vm.RegisterAsync(play: false);
    async void RegisterPlay_Click(object sender, RoutedEventArgs e) => await _vm.RegisterAsync(play: true);

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

    // 確認なしで削除（ピン留め中の行も消せる）
    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HistoryRow row) _vm.RemoveHistory(row);
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