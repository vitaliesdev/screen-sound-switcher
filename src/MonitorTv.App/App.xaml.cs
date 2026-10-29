using System.Windows;

namespace MonitorTv.App;

public partial class App : Application
{
    private static void LogError(Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(MonitorTv.Core.AppData.Dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(MonitorTv.Core.AppData.Dir, "error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    private const string InstanceName ="MonitorTv.App.SingleInstance";
    private const string ShowEventName = "MonitorTv.App.ShowWindow";

    private Mutex? _instanceLock;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;

    /// <summary>Replaces the main window with a fresh one, so a changed language is applied to every text.</summary>
    public void ReopenMainWindow()
    {
        var old = _window;
        // the old window lets go of the global shortcuts first, or the new one would find them taken
        old?.Release();
        MonitorTv.Core.Loc.Use(MonitorTv.Core.AppSettings.Load().Language);
        _window = new MainWindow();
        _window.Show();
        old?.CloseForReal();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MonitorTv.Core.Loc.Use(MonitorTv.Core.AppSettings.Load().Language);

        // A bug must never close a tray app silently: keep running, say what happened and keep the details in error.log.
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
            try { ToastWindow.Show(MonitorTv.Core.Loc.T("Algo ha fallado: {0}", args.Exception.Message), NoticeKind.Error); } catch { /* nothing more to do */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { LogError(args.Exception); args.SetObserved(); };

        // Text selection and caret come from the palette, not the Windows default.
        EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.TextBox), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            var box = (Wpf.Ui.Controls.TextBox)sender;
            box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentFillColorDefaultBrush");
            box.SelectionOpacity = 0.45;
            box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "TextFillColorPrimaryBrush");
        }));

        ToastWindow.Suppress = e.Args.Any(arg => arg.StartsWith("--snapshot"));
        Motion.Disabled = ToastWindow.Suppress && !e.Args.Contains("--frame");
        // Development aid: render the window to a PNG (off-screen) and exit, without touching the running instance.
        var args = e.Args;
        var toastShot = Array.IndexOf(args, "--snapshot-toast");
        if (toastShot >= 0 && toastShot + 2 < args.Length)
        {
            ToastWindow.SaveSnapshot(args[toastShot + 1], args[toastShot + 2], Enum.Parse<NoticeKind>(args.Length > toastShot + 3 ? args[toastShot + 3] : "Success"));
            return;
        }

        var makeIco = Array.IndexOf(args, "--make-ico");
        if (makeIco >= 0 && makeIco + 1 < args.Length) { IconArt.WriteIco(args[makeIco + 1]); Environment.Exit(0); }

        var iconShot = Array.IndexOf(args, "--snapshot-icons");
        if (iconShot >= 0 && iconShot + 1 < args.Length)
        {
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 760, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(Array.IndexOf(args, "--palettes") >= 0 ? IconArt.Palettes() : IconArt.ContactSheet());
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var file = System.IO.File.Create(args[iconShot + 1])) encoder.Save(file);
            Shutdown();
            return;
        }

        var rulesShot = Array.IndexOf(args, "--snapshot-rules");
        if (rulesShot >= 0 && rulesShot + 1 < args.Length)
        {
            var rulesWindow = new RulesWindow(MonitorTv.Core.AppSettings.Load(), new MonitorTv.Core.Profiles.ProfileStore().List().ToList())
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowInTaskbar = false,
                WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)),
            };
            rulesWindow.Show();
            rulesWindow.Dispatcher.BeginInvoke(() =>
            {
                rulesWindow.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)rulesWindow.ActualWidth, (int)rulesWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(rulesWindow);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var file = System.IO.File.Create(args[rulesShot + 1])) encoder.Save(file);
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }

        var menuShot = Array.IndexOf(args, "--snapshot-menu");
        if (menuShot >= 0 && menuShot + 1 < args.Length)
        {
            var shot = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowInTaskbar = false };
            shot.Show();
            shot.Dispatcher.BeginInvoke(() =>
            {
                try { shot.SaveMenuSnapshot(args[menuShot + 1], menuShot + 2 < args.Length ? args[menuShot + 2] : "tray"); }
                catch (Exception ex) { System.IO.File.WriteAllText(args[menuShot + 1] + ".err", ex.ToString()); }
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }

        var confirmShot = Array.IndexOf(args, "--snapshot-confirm");
        if (confirmShot >= 0 && confirmShot + 1 < args.Length)
        {
            ConfirmLayoutWindow.SaveSnapshot(args[confirmShot + 1], 15);
            return;
        }

        // Development aid: the monitor designer as a normal window, to try it with the real mouse (use with SSS_DATA).
        var openDesigner = Array.IndexOf(args, "--open-designer");
        if (openDesigner >= 0)
        {
            var name = openDesigner + 1 < args.Length && args[openDesigner + 1] != "-" ? args[openDesigner + 1] : null;
            var window = new LayoutDesignerWindow(new MonitorTv.Core.Profiles.ProfileStore(), name)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 100, Top = 60,
            };
            window.Closed += (_, _) => Shutdown();
            window.Show();
            return;
        }

        var designerShot = Array.IndexOf(args, "--snapshot-designer");
        if (designerShot >= 0 && designerShot + 1 < args.Length)
        {
            // --snapshot-designer <png> [profile|-] [--tick name]... [--save-as name]
            var rest = args.Skip(designerShot + 2).ToList();
            string? Value(string flag) => rest.IndexOf(flag) is var i and >= 0 && i + 1 < rest.Count ? rest[i + 1] : null;
            var ticks = rest.Select((a, i) => (a, i)).Where(t => t.a == "--tick" && t.i + 1 < rest.Count).Select(t => rest[t.i + 1]).ToList();
            LayoutDesignerWindow.SaveSnapshot(args[designerShot + 1], rest.Count > 0 && !rest[0].StartsWith("--") && rest[0] != "-" ? rest[0] : null,
                ticks.Count > 0 ? ticks : null, Value("--save-as"));
            return;
        }

        var snapshot = Array.IndexOf(args, "--snapshot");
        if (snapshot >= 0 && snapshot + 1 < args.Length)
        {
            var shot = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowInTaskbar = false };
            shot.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None; // Mica does not render into a bitmap
            var light = args.Contains("--light");
            if (light) Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            shot.Background = new System.Windows.Media.SolidColorBrush(light
                ? System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3)
                : System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20));
            var select = Array.IndexOf(args, "--select");
            if (select >= 0 && select + 1 < args.Length) shot.Height = 1300;
            var size = Array.IndexOf(args, "--size");
            if (size >= 0 && size + 1 < args.Length && args[size + 1].Split('x') is [var w, var h])
            {
                shot.Width = double.Parse(w);
                shot.Height = double.Parse(h);
            }
            shot.Show();
            shot.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (select >= 0 && select + 1 < args.Length)
                        shot.PreviewSelect(args[select + 1], args.Contains("--advanced"));
                    var switchTo = Array.IndexOf(args, "--switch-to");
                    if (switchTo >= 0 && switchTo + 1 < args.Length) shot.SwitchLanguage(args[switchTo + 1]);

                    var frame = Array.IndexOf(args, "--frame");
                    if (frame >= 0 && frame + 1 < args.Length)
                    {
                        // let the animation run for N ms (the dispatcher keeps rendering) and capture that moment
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(double.Parse(args[frame + 1])) };
                        timer.Tick += (_, _) =>
                        {
                            timer.Stop();
                            try { shot.SaveSnapshot(args[snapshot + 1]); }
                            catch (Exception ex) { System.IO.File.WriteAllText(args[snapshot + 1] + ".err", ex.ToString()); }
                            Shutdown();
                        };
                        timer.Start();
                        return;
                    }
                    shot.SaveSnapshot(args[snapshot + 1]);
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(args[snapshot + 1] + ".err", ex.ToString());
                }
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }

        // A copy started with its own data folder (SSS_DATA, for testing) is its own instance.
        var suffix = Environment.GetEnvironmentVariable("SSS_DATA") is { Length: > 0 } custom ? "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(custom)))[..8] : "";
        _instanceLock = new Mutex(true, InstanceName + suffix, out var isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + suffix);

        if (!isFirst)
        {
            // Already running (probably in the tray): ask that instance to show its window.
            _showEvent.Set();
            Shutdown();
            return;
        }

        _window = new MainWindow();
        _window.Show();

        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _window?.ShowFromOutside());
        }) { IsBackground = true };
        listener.Start();
    }
}
