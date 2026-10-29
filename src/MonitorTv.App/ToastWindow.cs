using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

public enum NoticeKind { Info, Success, Warning, Error }

/// <summary>
/// A small result notice near the bottom of the screen, for actions that run while the window is closed (a shortcut, the controller,
/// a rule). It never takes focus, so it is safe over a game, and errors stay up longer than successes.
/// </summary>
public sealed class ToastWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private static ToastWindow? _current;

    /// <summary>Set by the development snapshot commands so they never pop anything on the real screen.</summary>
    public static bool Suppress { get; set; }

    private readonly DispatcherTimer _timer = new();

    private ToastWindow(string text, NoticeKind kind)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 560;

        var dark = ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Light;
        var foreground = new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A));

        var (symbol, tint) = kind switch
        {
            NoticeKind.Success => (SymbolRegular.CheckmarkCircle24, Color.FromRgb(0x30, 0xD1, 0x58)),
            NoticeKind.Warning => (SymbolRegular.Warning24, Color.FromRgb(0xFF, 0x9F, 0x0A)),
            NoticeKind.Error => (SymbolRegular.ErrorCircle24, Color.FromRgb(0xFF, 0x45, 0x3A)),
            _ => (SymbolRegular.Info24, Color.FromRgb(0x0A, 0x84, 0xFF)),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SymbolIcon { Symbol = symbol, FontSize = 22, Foreground = new SolidColorBrush(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        row.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = text, Foreground = foreground, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460, VerticalAlignment = VerticalAlignment.Center,
        });

        Content = new Border
        {
            Child = row,
            Margin = new Thickness(16),
            Padding = new Thickness(18, 12, 20, 12),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x33, 0, 0, 0)),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0xF2, 0x26, 0x26, 0x26) : Color.FromArgb(0xF2, 0xF9, 0xF9, 0xF9)),
        };

        _timer.Interval = TimeSpan.FromSeconds(kind switch { NoticeKind.Error => 8, NoticeKind.Warning => 5, _ => 2.5 });
        _timer.Tick += (_, _) => Close();
        MouseLeftButtonUp += (_, _) => Close();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Bottom - ActualHeight - 56;
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_current == this) _current = null;
        };
    }

    /// <summary>Development aid: renders a notice off-screen to a PNG.</summary>
    public static void SaveSnapshot(string path, string text, NoticeKind kind)
    {
        var toast = new ToastWindow(text, kind) { Left = -20000, Top = 0 };
        toast.Show();
        toast.Dispatcher.BeginInvoke(() =>
        {
            toast.UpdateLayout();
            var element = (FrameworkElement)toast.Content;
            var dark = ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Light;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)), null,
                    new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var file = System.IO.File.Create(path)) encoder.Save(file);
            toast._timer.Stop();
            Application.Current.Shutdown();
        }, DispatcherPriority.ApplicationIdle);
    }

    public static void Show(string text, NoticeKind kind)
    {
        if (Suppress) return;
        _current?.Close();
        var toast = new ToastWindow(text, kind);
        _current = toast;
        toast.Show();
        Motion.Reveal((FrameworkElement)toast.Content, 10);
        toast._timer.Start();
    }
}
