using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MonitorTv.Core;
using MonitorTv.Core.Audio;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

/// <summary>Chooses which sound outputs one shortcut cycles through, like pressing "next headphones" without touching Windows.</summary>
public sealed class AudioCycleWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly List<System.Windows.Controls.CheckBox> _boxes = [];
    private readonly Wpf.Ui.Controls.TextBox _hotkey = new() { IsReadOnly = true, PlaceholderText = Loc.T("Haz clic aquí y pulsa la combinación, p. ej. Ctrl+Alt+H") };

    public AudioCycleWindow(AppSettings settings)
    {
        _settings = settings;

        Title = Loc.T("Alternar sonido");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;

        var intro = new System.Windows.Controls.TextBlock
        {
            Text = Loc.T("Un atajo pasa a la siguiente salida de sonido. Marca las que alternar, p. ej. altavoces y auriculares."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var list = new StackPanel();
        var active = AudioOutputs.List().Select(o => o.Name).ToList();
        // saved outputs first (in their cycle order), including ones that are not connected right now
        var all = _settings.AudioCycle.Outputs.Concat(active.Where(n => !_settings.AudioCycle.Outputs.Contains(n))).ToList();
        foreach (var name in all)
        {
            var connected = active.Contains(name);
            var box = new System.Windows.Controls.CheckBox
            {
                Content = connected ? name : Loc.T("{0} (no conectada ahora)", name),
                Tag = name,
                IsChecked = _settings.AudioCycle.Outputs.Contains(name),
                Margin = new Thickness(0, 0, 0, 6),
            };
            _boxes.Add(box);
            list.Children.Add(box);
        }

        _hotkey.Text = _settings.AudioCycle.Hotkey ?? "";
        _hotkey.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var text = HotkeyManager.Describe(Keyboard.Modifiers, key);
            if (text != null) _hotkey.Text = text;
        };

        var save = new Wpf.Ui.Controls.Button { Content = Loc.T("Guardar"), Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Wpf.Ui.Controls.Button { Content = Loc.T("Cancelar") };
        var clear = new Wpf.Ui.Controls.Button { Content = Loc.T("Quitar atajo"), Margin = new Thickness(8, 0, 0, 0) };
        clear.Click += (_, _) => _hotkey.Text = "";
        save.Click += (_, _) => { Commit(); DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;

        var hotkeyRow = new DockPanel { Margin = new Thickness(0, 8, 0, 18) };
        DockPanel.SetDock(clear, Dock.Right);
        hotkeyRow.Children.Add(clear);
        hotkeyRow.Children.Add(_hotkey);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(26, 12, 26, 22) };
        panel.Children.Add(intro);
        panel.Children.Add(list);
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = Loc.T("Atajo"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(hotkeyRow);
        panel.Children.Add(buttons);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = Loc.T("Alternar sonido"), ShowMaximize = false, ShowMinimize = false });
        Grid.SetRow(panel, 1);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetRow(root.Children[^1], 1);
        Content = root;
    }

    private void Commit()
    {
        _settings.AudioCycle.Outputs = _boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        _settings.AudioCycle.Hotkey = string.IsNullOrWhiteSpace(_hotkey.Text) ? null : _hotkey.Text;
        _settings.Save();
    }
}
