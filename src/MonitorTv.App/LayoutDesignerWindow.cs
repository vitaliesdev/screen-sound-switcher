using System.Windows;
using System.Windows.Controls;
using MonitorTv.Core;
using MonitorTv.Core.Monitors;
using MonitorTv.Core.Profiles;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

/// <summary>
/// Designs the screens of a profile on a canvas without touching the real screens: tick the monitors that belong to it
/// (on or off right now), drag them into place and save. Windows only validates the result; nothing is applied.
/// </summary>
public sealed class LayoutDesignerWindow : FluentWindow
{
    private readonly ProfileStore _store;
    private readonly string? _editing;
    private readonly DisplayConfig.Layout? _basis;
    private readonly List<LayoutBuilder.Monitor> _monitors = LayoutBuilder.Available();
    private readonly Dictionary<string, (int X, int Y)> _positions = [];
    private readonly Dictionary<string, (int W, int H)> _sizes = [];
    private readonly Dictionary<string, System.Windows.Controls.CheckBox> _checks = [];
    private readonly ArrangementView _arrangement = new() { Height = 330 };
    private readonly Wpf.Ui.Controls.TextBox _name = new() { PlaceholderText = Loc.T("Nombre del perfil nuevo"), Margin = new Thickness(0, 0, 0, 14) };
    private readonly System.Windows.Controls.TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = System.Windows.Media.Brushes.OrangeRed };
    private readonly Wpf.Ui.Controls.Button _save = new() { Content = Loc.T("Guardar"), Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 10, 0) };
    private bool _building = true;

    /// <summary>Name of the profile that was saved, or null when the window was cancelled.</summary>
    public string? SavedName { get; private set; }

    /// <param name="editing">An existing profile whose screens are being edited; null creates a new profile.</param>
    public LayoutDesignerWindow(ProfileStore store, string? editing = null)
    {
        _store = store;
        _editing = editing;
        _basis = editing != null ? store.Load(editing).Monitors : null;

        Title = Loc.T("Diseñar disposición de pantallas");
        Width = 940;
        Height = 660;
        MinWidth = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;

        var intro = new System.Windows.Controls.TextBlock
        {
            Text = Loc.T("Marca las pantallas que quieres en este perfil y colócalas arrastrándolas. No se cambia nada en tus pantallas hasta que apliques el perfil."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var list = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        foreach (var m in _monitors)
        {
            var check = new System.Windows.Controls.CheckBox { Margin = new Thickness(0, 0, 0, 12) };
            var label = new StackPanel();
            label.Children.Add(new System.Windows.Controls.TextBlock { Text = m.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{m.Width}×{m.Height} · {(m.Active ? Loc.T("encendida ahora") : Loc.T("apagada ahora"))}",
                FontSize = 12,
                Opacity = 0.7,
            });
            check.Content = label;
            check.Click += (_, _) => Rebuild();
            _checks[m.Target.DevicePath] = check;
            list.Children.Add(check);
            _sizes[m.Target.DevicePath] = (m.Width, m.Height);
        }
        list.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = _monitors.Count == 0 ? Loc.T("No hay pantallas conectadas.") : Loc.T("Solo aparecen las pantallas conectadas ahora, aunque estén apagadas."),
            FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
        });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var right = new StackPanel();
        right.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Loc.T("Arrastra las pantallas para colocarlas, o selecciónalas y usa las flechas. Clic derecho o la tecla P para elegir la principal."),
            FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        right.Children.Add(_arrangement);
        right.Children.Add(_message);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var cancel = new Wpf.Ui.Controls.Button { Content = Loc.T("Cancelar") };
        cancel.Click += (_, _) => Close();
        _save.Click += (_, _) => Save();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(_save);
        footer.Children.Add(cancel);

        var layout = new DockPanel { Margin = new Thickness(24, 8, 24, 20) };
        DockPanel.SetDock(intro, Dock.Top);
        layout.Children.Add(intro);
        if (editing == null)
        {
            DockPanel.SetDock(_name, Dock.Top);
            layout.Children.Add(_name);
        }
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(body);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = Loc.T("Diseñar disposición de pantallas"), ShowMaximize = false, ShowMinimize = false });
        Grid.SetRow(layout, 1);
        root.Children.Add(layout);
        Content = root;

        StartingSelection();
        _building = false;
        Rebuild();
    }

    /// <summary>Development aid: renders the editor off-screen to a PNG.</summary>
    public static void SaveSnapshot(string path, string? profile, IEnumerable<string>? tick = null, string? saveAs = null)
    {
        var window = new LayoutDesignerWindow(new ProfileStore(), profile)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0,
            WindowBackdropType = WindowBackdropType.None,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)),
        };
        window.Show();
        window.Dispatcher.BeginInvoke(() =>
        {
            // tick monitors by part of their name, as a click would
            foreach (var part in tick ?? [])
                foreach (var m in window._monitors.Where(m => m.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
                    window._checks[m.Target.DevicePath].IsChecked = true;
            if (tick != null) window.Rebuild();
            if (saveAs != null) { window._name.Text = saveAs; window.Save(); }
            window.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var file = System.IO.File.Create(path);
            encoder.Save(file);
            Application.Current.Shutdown();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>An existing profile starts with its own monitors where it puts them; a new one with the monitors that are on now.</summary>
    private void StartingSelection()
    {
        if (_basis != null)
        {
            var refreshed = DisplayIdentity.Refresh(_basis);
            foreach (var box in LayoutEditor.Read(refreshed))
            {
                var m = _monitors.FirstOrDefault(x => x.Target.TargetId == box.TargetId);
                if (m == null) continue;
                _checks[m.Target.DevicePath].IsChecked = true;
                _positions[m.Target.DevicePath] = (box.X, box.Y);
                _sizes[m.Target.DevicePath] = (box.Width, box.Height);
            }
            return;
        }
        foreach (var m in _monitors.Where(m => m.Active))
        {
            _checks[m.Target.DevicePath].IsChecked = true;
            _positions[m.Target.DevicePath] = (m.X, m.Y);
        }
    }

    /// <summary>Remembers where the user left each monitor, so ticking another one does not move the rest.</summary>
    private void RememberPositions()
    {
        if (_arrangement.GetLayout() is not { } current) return;
        foreach (var box in LayoutEditor.Read(current))
        {
            var m = _monitors.FirstOrDefault(x => x.Target.TargetId == box.TargetId);
            if (m == null) continue;
            _positions[m.Target.DevicePath] = (box.X, box.Y);
            _sizes[m.Target.DevicePath] = (box.Width, box.Height);
        }
    }

    private void Rebuild()
    {
        if (_building) return;
        RememberPositions();
        _message.Text = "";

        var selected = _monitors.Where(m => _checks[m.Target.DevicePath].IsChecked == true).ToList();
        _save.IsEnabled = selected.Count > 0;
        if (selected.Count == 0) { _arrangement.Load(null); return; }

        var placed = new List<(LayoutBuilder.Monitor Monitor, int X, int Y)>();
        foreach (var m in selected.Where(m => _positions.ContainsKey(m.Target.DevicePath)))
            placed.Add((m, _positions[m.Target.DevicePath].X, _positions[m.Target.DevicePath].Y));
        // a monitor added now goes to the right of the others until it is dragged somewhere else
        foreach (var m in selected.Where(m => !_positions.ContainsKey(m.Target.DevicePath)))
        {
            var edge = placed.Count == 0 ? 0 : placed.Max(p => p.X + _sizes[p.Monitor.Target.DevicePath].W);
            placed.Add((m, edge, 0));
        }

        try
        {
            _arrangement.Load(LayoutBuilder.Build(placed, _basis));
            _arrangement.Heal(); // unticking a monitor can leave another one touching nothing
        }
        catch (Exception ex)
        {
            _arrangement.Load(null);
            _message.Text = ex.Message;
            _save.IsEnabled = false;
        }
    }

    private void Save()
    {
        var name = _editing ?? _name.Text.Trim();
        if (_editing == null)
        {
            if (name.Length == 0 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                _message.Text = Loc.T("Escribe un nombre válido para el perfil.");
                return;
            }
            if (_store.List().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _message.Text = Loc.T("Ya existe un perfil con ese nombre.");
                return;
            }
        }

        try
        {
            if (_arrangement.GetLayout() is not { } layout) throw new InvalidOperationException(Loc.T("Elige al menos una pantalla."));
            try { DisplayConfig.Validate(layout); }
            catch (Exception ex) { throw new InvalidOperationException(Loc.T("Windows no acepta esta disposición: {0}", ex.Message)); }

            var profile = _editing != null ? _store.Load(_editing) : new Profile { Name = name };
            profile.Monitors = DisplayIdentity.WithTargets(layout);
            _store.Save(profile);
            SavedName = profile.Name;
            try { DialogResult = true; }
            catch (InvalidOperationException) { Close(); } // not shown as a dialog (development snapshot)
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
        }
    }
}
