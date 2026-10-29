using MonitorTv.Core;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

/// <summary>
/// "Keep these changes?" with a countdown, shown right after the monitor layout changes. Nothing to click means the
/// old layout comes back, so a black screen never needs the user to see anything.
/// </summary>
public sealed class ConfirmLayoutWindow : FluentWindow
{
    private readonly TaskCompletionSource<bool> _result = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Windows.Controls.TextBlock _countdown = new() { FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly System.Windows.Controls.ProgressBar _bar = new() { Height = 6, Margin = new Thickness(0, 14, 0, 18) };
    private int _left;

    private ConfirmLayoutWindow(int seconds)
    {
        _left = seconds;
        _bar.Maximum = seconds;
        _bar.Value = seconds;

        Title = Loc.T("Confirmar disposición de pantallas");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;

        var keep = new Wpf.Ui.Controls.Button { Content = Loc.T("Mantener"), Appearance = ControlAppearance.Primary, MinWidth = 140, Margin = new Thickness(0, 0, 10, 0) };
        var revert = new Wpf.Ui.Controls.Button { Content = Loc.T("Volver a la anterior"), MinWidth = 160 };
        keep.Click += (_, _) => Finish(true);
        revert.Click += (_, _) => Finish(false);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Warning24, FontSize = 30, Margin = new Thickness(0, 0, 12, 0), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x0A)) });
        header.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Loc.T("¿Mantener esta disposición de pantallas?"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(keep);
        buttons.Children.Add(revert);

        var panel = new StackPanel { Margin = new Thickness(28, 20, 28, 24) };
        panel.Children.Add(header);
        panel.Children.Add(_countdown);
        panel.Children.Add(_bar);
        panel.Children.Add(buttons);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = AppInfo.Name, Icon = new ImageIcon { Source = IconArt.Render(32), Width = 16, Height = 16 }, ShowMaximize = false, ShowMinimize = false, ShowClose = false });
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Content = root;

        UpdateText();
        _timer.Tick += (_, _) =>
        {
            _left--;
            _bar.Value = _left;
            UpdateText();
            if (_left <= 0) Finish(false);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(false); };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _result.TrySetResult(false);
            if (_pending == this) _pending = null;
        };
    }

    private static ConfirmLayoutWindow? _pending;

    /// <summary>The emergency restore put the screens back by itself: whoever is waiting for the answer must not keep a stale layout.</summary>
    public static void CancelPending() => _pending?.Finish(false);

    private void UpdateText() =>
        _countdown.Text = Loc.T("Si no pulsas «Mantener», se restaurará la disposición anterior en {0} s. Si no ves esta ventana, no hagas nada: volverá sola.", _left);

    private void Finish(bool keep)
    {
        _timer.Stop();
        _result.TrySetResult(keep);
        Close();
    }

    /// <summary>Development aid: renders the dialog off-screen to a PNG.</summary>
    public static void SaveSnapshot(string path, int seconds)
    {
        var window = new ConfirmLayoutWindow(seconds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0,
            WindowBackdropType = WindowBackdropType.None,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)),
        };
        window.Show();
        window.Dispatcher.BeginInvoke(() =>
        {
            window.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var file = System.IO.File.Create(path);
            encoder.Save(file);
            window._timer.Stop();
            Application.Current.Shutdown();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Shows the dialog on the primary screen and completes with the user's choice (false on timeout).</summary>
    public static Task<bool> Ask(int seconds)
    {
        var window = new ConfirmLayoutWindow(seconds);
        _pending = window;
        window.Show();
        window.Activate();
        window._timer.Start();
        SystemSounds.Exclamation.Play();
        return window._result.Task;
    }
}
