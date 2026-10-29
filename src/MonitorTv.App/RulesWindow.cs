using System.Windows;
using System.Windows.Controls;
using MonitorTv.Core;
using MonitorTv.Core.Audio;
using MonitorTv.Core.Devices;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

/// <summary>
/// Editor for automatic rules: "when this program opens (or the headset switches on), apply a profile or change the sound output;
/// when it closes (or switches off), do something else or go back to how things were".
/// </summary>
public sealed class RulesWindow : FluentWindow
{
    private sealed record Choice(string Label, string? Value)
    {
        public override string ToString() => Label;
    }

    private readonly AppSettings _settings;
    private readonly IReadOnlyList<string> _profiles;
    private readonly List<string> _outputs;
    private readonly bool _arctisPresent;
    private readonly StackPanel _rows = new();

    public RulesWindow(AppSettings settings, IReadOnlyList<string> profiles)
    {
        _settings = settings;
        _profiles = profiles;
        _outputs = AudioOutputs.List().Select(o => o.Name).ToList();
        // the headset option only exists for people who actually have the base station plugged in
        _arctisPresent = ArctisHeadset.BasePresent;

        Title = Loc.T("Reglas automáticas");
        Width = 940;
        Height = 620;
        MinWidth = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;

        var intro = new System.Windows.Controls.TextBlock
        {
            Text = Loc.T("Cambia de perfil o de salida de sonido cuando un programa se abre o se cierra, por ejemplo Steam Big Picture → perfil TV, y al cerrarlo volver a como estaba. Un programa se reconoce por el nombre de su proceso (steam.exe) o por parte del título de su ventana.") +
                   (_arctisPresent ? " " + Loc.T("Con «Auriculares Arctis» la regla se activa al encender o apagar los auriculares.") : ""),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var add = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("Añadir regla"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Add24 },
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, 0, 10, 0),
        };
        add.Click += (_, _) =>
        {
            var rule = new ProgramRule();
            _settings.Rules.Add(rule);
            _rows.Children.Add(Row(rule));
        };

        var done = new Wpf.Ui.Controls.Button { Content = Loc.T("Listo") };
        done.Click += (_, _) => Close();

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(add);
        footer.Children.Add(done);

        var layout = new DockPanel { Margin = new Thickness(24, 8, 24, 20) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(intro);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = Loc.T("Reglas automáticas"), ShowMaximize = false, ShowMinimize = false });
        Grid.SetRow(layout, 1);
        root.Children.Add(layout);
        Content = root;

        foreach (var rule in _settings.Rules) _rows.Children.Add(Row(rule));
        Closed += (_, _) => _settings.Save();
    }

    private UIElement Row(ProgramRule rule)
    {
        // line 1: what to watch
        var enabled = new System.Windows.Controls.CheckBox { IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("Regla activa") };
        enabled.Click += (_, _) => rule.Enabled = enabled.IsChecked == true;

        var kinds = new List<string> { Loc.T("Proceso"), Loc.T("Título de ventana") };
        // keep the headset option when a rule already uses it, even if the base is unplugged right now
        var showHeadset = _arctisPresent || rule.Match == "arctis";
        if (showHeadset) kinds.Add(Loc.T("Auriculares Arctis"));
        var match = new System.Windows.Controls.ComboBox { ItemsSource = kinds, Width = 180 };
        match.SelectedIndex = rule.Match switch { "title" => 1, "arctis" when showHeadset => 2, _ => 0 };

        var text = new Wpf.Ui.Controls.TextBox { Text = rule.Text, PlaceholderText = "steam.exe  /  Big Picture" };
        text.TextChanged += (_, _) => rule.Text = text.Text;

        var delete = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 }, ToolTip = Loc.T("Quitar la regla") };

        // line 2: what to do
        var startCaption = Caption("");
        var stopCaption = Caption("");
        var onStart = ActionCombo(isStop: false, rule.OnStart, v => rule.OnStart = v);
        var onStop = ActionCombo(isStop: true, rule.OnStop, v => rule.OnStop = v);

        void MatchChanged()
        {
            rule.Match = match.SelectedIndex switch { 1 => "title", 2 => "arctis", _ => "process" };
            var headset = rule.Match == "arctis";
            text.IsEnabled = !headset;
            text.PlaceholderText = headset ? Loc.T("Se detecta por la base USB, no hace falta nombre") : "steam.exe  /  Big Picture";
            startCaption.Text = headset ? Loc.T("Al encender los auriculares") : Loc.T("Al abrirse");
            stopCaption.Text = headset ? Loc.T("Al apagar los auriculares") : Loc.T("Al cerrarse");
        }
        match.SelectionChanged += (_, _) => MatchChanged();
        MatchChanged();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        text.Margin = new Thickness(10, 0, 10, 0);
        match.Margin = new Thickness(10, 0, 0, 0);
        PlaceInColumns(top, enabled, match, text, delete);

        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var startBox = new StackPanel();
        startBox.Children.Add(startCaption);
        startBox.Children.Add(onStart);
        var stopBox = new StackPanel();
        stopBox.Children.Add(stopCaption);
        stopBox.Children.Add(onStop);
        Grid.SetColumn(stopBox, 2);
        actions.Children.Add(startBox);
        actions.Children.Add(stopBox);

        var card = new Border
        {
            Child = new StackPanel { Children = { top, actions } },
            Padding = new Thickness(16, 14, 16, 16),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        delete.Click += (_, _) => { _settings.Rules.Remove(rule); _rows.Children.Remove(card); };
        return card;
    }

    private static void PlaceInColumns(Grid grid, params UIElement[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
    }

    private static System.Windows.Controls.TextBlock Caption(string text) => new()
    {
        Text = text, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4),
    };

    /// <summary>
    /// One combo for "what happens": nothing, switch to a profile, switch the sound output, or (when closing) go back to how things were.
    /// Stored as null, a profile name, "audio:&lt;output&gt;" or "@previous".
    /// </summary>
    private System.Windows.Controls.ComboBox ActionCombo(bool isStop, string? current, Action<string?> set)
    {
        var options = new List<Choice> { new(Loc.T("— nada —"), null) };
        if (isStop) options.Add(new Choice(Loc.T("Volver a como estaba (perfil y sonido)"), RulesEngine.Previous));
        options.AddRange(_profiles.Select(p => new Choice(Loc.T("Perfil: {0}", p), p)));

        var outputs = new List<string>(_outputs);
        // an output chosen earlier that is not connected right now stays selectable instead of silently disappearing
        if (current != null && current.StartsWith(RulesEngine.AudioPrefix) && !outputs.Contains(current[RulesEngine.AudioPrefix.Length..]))
            outputs.Add(current[RulesEngine.AudioPrefix.Length..]);
        options.AddRange(outputs.Select(o => new Choice(Loc.T("Sonido: {0}", o), RulesEngine.AudioPrefix + o)));

        var combo = new System.Windows.Controls.ComboBox { ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
        combo.SelectionChanged += (_, _) => set((combo.SelectedItem as Choice)?.Value);
        return combo;
    }
}
