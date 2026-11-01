using MonitorTv.Core;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly System.Windows.Shapes.Ellipse _ring;
    private readonly System.Windows.Controls.TextBlock _number = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly int _total;
    private int _left;

    private ConfirmLayoutWindow(int seconds)
    {
        _left = seconds;
        _total = seconds;
        _ring = MakeRing(seconds);

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

        var track = new System.Windows.Shapes.Ellipse
        {
            Width = 96, Height = 96, StrokeThickness = 8,
            Stroke = (System.Windows.Media.Brush)FindResource("ControlStrokeColorDefaultBrush"),
        };
        var dial = new Grid { Width = 112, Height = 112, Margin = new Thickness(0, 14, 0, 14), HorizontalAlignment = HorizontalAlignment.Center };
        dial.Children.Add(track);
        dial.Children.Add(_ring);
        dial.Children.Add(_number);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_number, System.Windows.Automation.AutomationLiveSetting.Polite);
        _countdown.TextAlignment = TextAlignment.Center;
        buttons.HorizontalAlignment = HorizontalAlignment.Center;

        var panel = new StackPanel { Margin = new Thickness(28, 20, 28, 24) };
        panel.Children.Add(header);
        panel.Children.Add(dial);
        panel.Children.Add(_countdown);
        panel.Children.Add(new Border { Height = 18 });
        panel.Children.Add(buttons);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = AppInfo.Name, Icon = IconArt.TitleIcon(), ShowMaximize = false, ShowMinimize = false, ShowClose = false });
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Content = root;

        UpdateText();
        _timer.Tick += (_, _) =>
        {
            _left--;
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

    private static readonly System.Windows.Media.Brush Amber = Frozen(0xFF, 0x9F, 0x0A);
    private static readonly System.Windows.Media.Brush Red = Frozen(0xFF, 0x45, 0x3A);

    private static System.Windows.Media.Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The ring unwinds on its own clock (a linear dash-offset animation, so it never stutters when the 1 s timer is late).
    /// With animations off it steps once a second instead.
    /// </summary>
    private System.Windows.Shapes.Ellipse MakeRing(int seconds)
    {
        // dash lengths are in multiples of the stroke width: circumference / thickness
        var full = Math.PI * 96 / 8;
        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = 96, Height = 96, StrokeThickness = 8, Stroke = Amber,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { full, full + 2 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.RotateTransform(-90),
        };
        if (Motion.Enabled)
        {
            ring.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, full, TimeSpan.FromSeconds(seconds)));
        }
        return ring;
    }

    private void UpdateText()
    {
        _number.Text = _left.ToString(CultureInfo.CurrentCulture);
        _ring.Stroke = _left <= 3 ? Red : Amber;
        if (!Motion.Enabled)
            _ring.StrokeDashOffset = Math.PI * 96 / 8 * (_total - _left) / Math.Max(1, _total);
        _countdown.Text = Loc.T("Sin «Mantener», vuelve la disposición anterior en {0} s. Si no ves esta ventana, espera.", _left);
    }

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
