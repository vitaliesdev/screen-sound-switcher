using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonitorTv.Core;
using MonitorTv.Core.Audio;
using MonitorTv.Core.Monitors;
using MonitorTv.Core.Profiles;
using MonitorTv.Core.Tv;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

public partial class MainWindow : FluentWindow
{
    private readonly ProfileStore _store = new();
    private bool _reallyClosing;
    private HotkeyManager? _hotkeys;
    private string? _shownProfile;
    private Updater.Release? _update;
    private string? _active;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ControllerListener _controller = new();
    private RulesEngine? _rulesEngine;
    private bool _applying;

    public sealed record ProfileItem(Profile Profile)
    {
        public string Name => Profile.Name;

        public string Summary =>
            $"{(Profile.Monitors != null ? Loc.T("Monitores") : Loc.T("Sin monitores"))} · {(Profile.AudioOutput ?? Loc.T("Audio sin cambios"))}";

        public string HotkeyText => Profile.Hotkey ?? "";

        private int Screens => Profile.Monitors == null ? 0 : (int)Profile.Monitors.PathCount;

        private bool UsesTv => Profile.TvInput != null || Profile.TvPower != null;

        public SymbolRegular Symbol => UsesTv ? SymbolRegular.Tv24 : Screens == 0 ? SymbolRegular.Speaker224 : SymbolRegular.Desktop24;

        public string Kind => UsesTv ? Loc.T("Tele") : Screens == 0 ? Loc.T("Solo sonido") : Screens == 1 ? Loc.T("1 pantalla") : Loc.T("{0} pantallas", Screens);

        public Brush TileBrush => (Brush)Application.Current.FindResource(
            UsesTv ? "TilePurple" : Screens == 0 ? "TilePink" : Screens >= 2 ? "TileTeal" : "TileBlue");
    }

    public MainWindow()
    {
        InitializeComponent();
        // anything on the profile screen that can be edited marks the profile as changed
        Details.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateDirty()));
        Details.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => UpdateDirty()));
        Details.AddHandler(System.Windows.Controls.Primitives.ToggleButton.ClickEvent, new RoutedEventHandler((_, _) => UpdateDirty()));
        Arrangement.Changed += UpdateDirty;
        AudioCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => SyncAudioChips()));
        LocTree.Translate(this);
        ApplyTheme();
        var accent = Color.FromRgb(0x0A, 0x84, 0xFF);
        ApplicationAccentColorManager.Apply(accent, ApplicationThemeManager.GetAppTheme());
        ApplicationThemeManager.Changed += (theme, _) => ApplicationAccentColorManager.Apply(accent, theme);
        InitControllerUi();
        Motion.Mode = _settings.Animations;

        var icon = BuildIcon();
        Icon = icon;
        TrayIcon.Icon = IconArt.RenderSimple(32);
        AppTitleBar.Icon = IconArt.TitleIcon();

        Loaded += (_, _) =>
        {
            try { _store.MigrateTargets(); } catch { /* keep going with the profiles as they are */ }
            Reload();
            SetUpFooter();
            _rulesEngine = new RulesEngine(() => _settings.Rules, () => _active, ApplyByName, Status);
            EnsureLastKnownGood();
            StartScreenGuard();
            ShowSafetyNoticeOnce();
            // Started with Windows (--tray): stay in the tray until opened.
            if (Environment.GetCommandLineArgs().Contains("--tray"))
                Dispatcher.BeginInvoke(Hide, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private Profile? Selected => (ProfileList.SelectedItem as ProfileItem)?.Profile;

    private void Reload(string? select = null)
    {
        var damaged = new List<string>();
        var items = _store.LoadAll(damaged.Add).Select(p => new ProfileItem(p)).ToList();
        // replacing the list empties the selection for a moment: that is not the person leaving the profile (ShowDetails runs below)
        _revertingSelection = true;
        try
        {
            ProfileList.ItemsSource = items;
            EmptyText.Text = items.Count == 0
                ? Loc.T("Aún no hay perfiles. Colócalo todo en Windows y pulsa «Crear con los monitores actuales».")
                : Loc.T("Selecciona un perfil de la lista para editarlo.");
            ProfileList.SelectedItem = items.FirstOrDefault(i => i.Name == select);
        }
        finally { _revertingSelection = false; }
        RebuildTrayMenu(items);
        RegisterHotkeys(items);
        ShowDetails();
        // after the window has finished loading, so the notice is not overwritten by the initial footer text
        if (damaged.Count > 0)
            Dispatcher.BeginInvoke(() => Status(damaged.Count == 1
                    ? Loc.T("Perfil '{0}' dañado, no se puede leer. No se borró: arréglalo o quítalo de la carpeta {1}.", damaged[0], _store.Root)
                    : Loc.T("{0} perfiles dañados ({1}), no se pueden leer. No se borraron: están en la carpeta {2}.", damaged.Count, string.Join(", ", damaged), _store.Root),
                    NoticeKind.Warning),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // --- Unsaved changes -------------------------------------------------------------------

    private string _baseline = "";
    private bool _dirty;
    private bool _loadingDetails;
    private ProfileItem? _current;
    private bool _revertingSelection;

    private void ShowDetails()
    {
        _loadingDetails = true; // filling the screen in is not an edit
        try { ShowDetailsCore(); }
        finally { _loadingDetails = false; }
        _current = ProfileList.SelectedItem as ProfileItem;
        _baseline = CollectFromUi() is { } c ? JsonSerializer.Serialize(c) : "";
        _dirty = false;
        DirtyBadge.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = false;
    }

    /// <summary>The selected profile as it is on screen right now, without saving it.</summary>
    private Profile? CollectFromUi()
    {
        if (Selected is not { } p) return null;
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p))!;
        ApplyUi(copy);
        return copy;
    }

    /// <summary>Copies what the screen shows into a profile (the part of the screen that belongs to the profile).</summary>
    private void ApplyUi(Profile p)
    {
        var audio = AudioCombo.Text.Trim();
        p.AudioOutput = audio.Length == 0 ? null : audio;
        if (p.Monitors != null) p.Monitors = Arrangement.GetLayout();
        p.Hotkey = HotkeyBox.Text.Length == 0 ? null : HotkeyBox.Text;
        p.ControllerCombo = _comboMask;
        p.TvPower = TvPowerOptions[Math.Max(0, TvPowerCombo.SelectedIndex)].Value;
        p.TvInput = TvInputOptions[Math.Max(0, TvInputCombo.SelectedIndex)].Value;
        p.Launch = LaunchBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>The active outputs as chips, a click away; the editable box stays for outputs that are not connected yet.</summary>
    private void BuildAudioChips(IEnumerable<string> outputs)
    {
        AudioChips.Children.Clear();
        foreach (var name in outputs)
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = name,
                Tag = name,
                Style = (Style)FindResource("Chip"),
                MaxWidth = 260,
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, name);
            chip.Click += (_, _) => { AudioCombo.Text = name; SyncAudioChips(); };
            AudioChips.Children.Add(chip);
        }
        SyncAudioChips();
    }

    private void SyncAudioChips()
    {
        var current = AudioCombo.Text.Trim();
        foreach (System.Windows.Controls.Primitives.ToggleButton chip in AudioChips.Children)
            chip.IsChecked = current.Length > 0 && ((string)chip.Tag).Contains(current, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateDirty()
    {
        if (_loadingDetails) return;
        _dirty = Selected != null && (CollectFromUi() is { } c ? JsonSerializer.Serialize(c) : "") != _baseline;
        DirtyBadge.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = _dirty;
    }

    /// <summary>
    /// Asks what to do with unsaved changes before something throws them away. True when it is fine to go on (saved, discarded,
    /// or there was nothing to save); false when the person chose to stay.
    /// </summary>
    private async Task<bool> ConfirmLeaveAsync()
    {
        if (!_dirty || Selected is not { } p) return true;
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Cambios sin guardar"),
            Content = Loc.T("El perfil '{0}' tiene cambios sin guardar.", p.Name),
            PrimaryButtonText = Loc.T("Guardar"),
            SecondaryButtonText = Loc.T("Descartar"),
            CloseButtonText = Loc.T("Cancelar"),
        };
        switch (await dialog.ShowDialogAsync())
        {
            case Wpf.Ui.Controls.MessageBoxResult.Primary:
                Save_Click(this, new RoutedEventArgs());
                return !_dirty; // a failed save keeps the person where the changes still are
            case Wpf.Ui.Controls.MessageBoxResult.Secondary:
                _dirty = false;
                return true;
            default:
                return false;
        }
    }

    private void ShowDetailsCore()
    {
        var p = Selected;
        EmptyText.Visibility = p == null ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        DetailHeader.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        if (p == null) return;

        if (_shownProfile != p.Name && Details.Content is FrameworkElement content)
        {
            _shownProfile = p.Name;
        SetGroupOpen("Tv", p.TvPower != null || p.TvInput != null);
        SetGroupOpen("Launch", p.Launch.Any(l => !string.IsNullOrWhiteSpace(l)));
            // The pane scrolls back to its top and the settings cascade in, so it is obvious another profile is showing.
            // Arrowing through the list is a keyboard action repeated all day: it changes instantly, with no movement.
            if (System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice)
                Details.ScrollToVerticalOffset(0);
            else
            {
                Motion.ScrollToTop(Details);
                if (content is Panel panel) Motion.Cascade(panel);
                Motion.Pop(DetailTile);
            }
        }
        DetailName.Text = p.Name;
        if (ProfileList.SelectedItem is ProfileItem selectedItem)
        {
            DetailTile.Background = selectedItem.TileBrush;
            DetailIcon.Symbol = selectedItem.Symbol;
            DetailKind.Text = selectedItem.Kind;
        }
        ApplyMode();
        MonitorsText.Text = p.Monitors != null
            ? (p.Monitors.PathCount == 1 ? Loc.T("1 pantalla guardada") : Loc.T("{0} pantallas guardadas", p.Monitors.PathCount))
            : Loc.T("No se cambian los monitores");

        var outputs = AudioOutputs.List().Select(o => o.Name).ToList();
        AudioCombo.ItemsSource = outputs;
        AudioCombo.Text = p.AudioOutput ?? "";
        BuildAudioChips(outputs);
        HotkeyBox.Text = p.Hotkey ?? "";
        SetCombo(p.ControllerCombo);
        Arrangement.Load(p.Monitors);
        ArrangementCard.Visibility = Arrangement.HasMonitors ? Visibility.Visible : Visibility.Collapsed;

        TvPowerCombo.ItemsSource = TvPowerOptions.Select(o => o.Label).ToList();
        TvPowerCombo.SelectedIndex = Math.Max(0, TvPowerOptions.FindIndex(o => o.Value == p.TvPower));
        TvInputCombo.ItemsSource = TvInputOptions.Select(o => o.Label).ToList();
        TvInputCombo.SelectedIndex = Math.Max(0, TvInputOptions.FindIndex(o => o.Value == p.TvInput));
        LaunchBox.Text = string.Join(Environment.NewLine, p.Launch);

        var tv = TvSettings.Load();
        TvIp.Text = tv.Ip;
        TvMac.Text = tv.Mac;
        TvPairText.Text = tv.ClientKey == "" ? Loc.T("Sin emparejar.") : Loc.T("Emparejada.");
        UpdateGroupSummaries();
    }

    // properties, not static fields: the labels follow the language chosen at startup
    private static List<(string Label, string? Value)> TvPowerOptions =>
        [(Loc.T("Encendido: sin cambios"), null), (Loc.T("Encender la tele"), "on"), (Loc.T("Apagar la tele"), "off")];

    private static List<(string Label, string? Value)> TvInputOptions =>
        [(Loc.T("Entrada: sin cambios"), null), ("HDMI 1", "HDMI_1"), ("HDMI 2", "HDMI_2"), ("HDMI 3", "HDMI_3"), ("HDMI 4", "HDMI_4"), (Loc.T("Televisión"), "TV")];

    private void TvSave_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var tv = TvSettings.Load();
        tv.Ip = TvIp.Text.Trim();
        tv.Mac = TvMac.Text.Trim();
        tv.Save();
        Status(Loc.T("Datos de la tele guardados."), NoticeKind.Success);
    });

    private async void TvPair_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TvSave_Click(sender, e);
            Status(Loc.T("Acepta el aviso en la tele..."));
            using var tv = await LgTv.Connect(TvSettings.Load(), TimeSpan.FromSeconds(10), pairing: true);
            TvPairText.Text = Loc.T("Emparejada.");
            Status(Loc.T("Tele emparejada."), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            Status(Loc.T("Error: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private void RegisterHotkeys(IEnumerable<ProfileItem> items)
    {
        _hotkeys ??= new HotkeyManager(this);
        _hotkeys.Clear();
        var failed = new List<string>();
        if (!_hotkeys.Register(_settings.EmergencyHotkey, EmergencyRestore))
            failed.Add(Loc.T("restaurar pantallas ({0})", _settings.EmergencyHotkey));
        if (_settings.AudioCycle is { Hotkey: { } cycleKey, Outputs.Count: > 0 } && !_hotkeys.Register(cycleKey, CycleAudio))
            failed.Add(Loc.T("alternar sonido ({0})", cycleKey));
        foreach (var item in items.Where(i => i.Profile.Hotkey != null))
        {
            var profile = item.Profile;
            if (!_hotkeys.Register(profile.Hotkey!, async () => await ApplyProfile(profile)))
                failed.Add($"{profile.Name} ({profile.Hotkey})");
        }
        _controller.Clear();
        foreach (var item in items.Where(i => i.Profile.ControllerCombo is > 0))
        {
            var profile = item.Profile;
            if (!_controller.Register((ushort)profile.ControllerCombo!.Value, () => Dispatcher.BeginInvoke(async () => await ApplyProfile(profile))))
                failed.Add(Loc.T("{0} (mando)", profile.Name));
        }
        if (failed.Count > 0)
            Status(Loc.T("No se pudo registrar el atajo de: {0} (ya en uso o repetido).", string.Join(", ", failed)), NoticeKind.Warning);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var text = HotkeyManager.Describe(System.Windows.Input.Keyboard.Modifiers, key);
        if (text != null) HotkeyBox.Text = text;
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e) => HotkeyBox.Text = "";

    /// <summary>Basic mode shows only monitors, sound and the shortcut; advanced adds TV, programs and the rest.</summary>
    private void ApplyMode()
    {
        AdvancedToggle.IsChecked = _settings.AdvancedMode;
        var advanced = _settings.AdvancedMode ? Visibility.Visible : Visibility.Collapsed;
        AdvancedGroup.Visibility = advanced;
        ControllerCard.Visibility = advanced;
        RulesButton.Visibility = advanced;
    }

    private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.AdvancedMode = AdvancedToggle.IsChecked == true;
        _settings.Save();
        ApplyMode();
    }

    private void CycleAudio()
    {
        try
        {
            var output = AudioOutputs.CycleNext(_settings.AudioCycle.Outputs);
            if (output == null) Status(Loc.T("Ninguna de las salidas elegidas está conectada."), NoticeKind.Warning); else Status(Loc.T("Sonido: {0}", output.Name), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            Status(Loc.T("No se pudo cambiar el sonido: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private void AudioCycle_Click(object sender, RoutedEventArgs e)
    {
        if (new AudioCycleWindow(_settings) { Owner = this }.ShowDialog() == true)
            Reload(Selected?.Name); // re-registers the shortcut and refreshes the tray menu
    }

    private void ApplyByName(string name)
    {
        try { _ = ApplyProfile(_store.Load(name)); }
        catch (Exception ex) { Status(Loc.T("Regla: no se pudo aplicar '{0}': {1}", name, ex.Message), NoticeKind.Error); }
    }

    private void Rules_Click(object sender, RoutedEventArgs e) =>
        new RulesWindow(_settings, _store.List().ToList()) { Owner = this }.ShowDialog();

    // --- Controller combination: presets or a custom set of buttons -----------------------

    private int? _comboMask;
    private bool _comboUpdating;
    private const int CustomIndex = 4;

    private void InitControllerUi()
    {
        ControllerButtons.Children.Clear();
        ControllerPreset.ItemsSource = new[]
        {
            Loc.T("Sin combinación"),
            Loc.T("{0}  ·  recomendada", ControllerListener.Presets[0].Label),
            ControllerListener.Presets[1].Label,
            ControllerListener.Presets[2].Label,
            Loc.T("Personalizada…"),
        };

        foreach (var (mask, name) in ControllerListener.Buttons)
        {
            var toggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = Loc.T(name), Tag = (int)mask, MinWidth = 46, Margin = new Thickness(0, 0, 6, 6),
            };
            toggle.Click += (_, _) => ComboToggled();
            ControllerButtons.Children.Add(toggle);
        }
    }

    private void SetCombo(int? mask)
    {
        _comboUpdating = true;
        _comboMask = mask is > 0 ? mask : null;

        var preset = _comboMask == null ? -1 : Array.FindIndex(ControllerListener.Presets, p => p.Mask == _comboMask);
        var index = _comboMask == null ? 0 : preset >= 0 ? preset + 1 : CustomIndex;
        ControllerPreset.SelectedIndex = index;
        ControllerCustom.Visibility = index == CustomIndex ? Visibility.Visible : Visibility.Collapsed;
        foreach (System.Windows.Controls.Primitives.ToggleButton t in ControllerButtons.Children)
            t.IsChecked = _comboMask is { } m && (m & (int)t.Tag) != 0;

        _comboUpdating = false;
        UpdateControllerHint();
    }

    private void ControllerPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_comboUpdating || ControllerPreset.SelectedIndex < 0) return;
        var index = ControllerPreset.SelectedIndex;
        if (index == CustomIndex)
        {
            ControllerCustom.Visibility = Visibility.Visible;
            UpdateControllerHint();
        }
        else
        {
            SetCombo(index == 0 ? null : ControllerListener.Presets[index - 1].Mask);
        }
    }

    private void ComboToggled()
    {
        var mask = 0;
        foreach (System.Windows.Controls.Primitives.ToggleButton t in ControllerButtons.Children)
            if (t.IsChecked == true) mask |= (int)t.Tag;
        _comboMask = mask > 0 ? mask : null;
        UpdateControllerHint();
    }

    private void UpdateControllerHint()
    {
        if (_comboMask is not { } mask)
        {
            ControllerHint.Text = Loc.T("Sin combinación: el mando no activa este perfil. Elige una o crea la tuya.");
            return;
        }

        var text = Loc.T("Se activa manteniendo {0} medio segundo.", ControllerListener.Describe((ushort)mask));
        var isPreset = ControllerListener.Presets.Any(p => p.Mask == mask);
        if (isPreset)
            text += " " + Loc.T("Evita el botón Guía de Steam y los botones sueltos o de menú que usan los juegos.");
        if (System.Numerics.BitOperations.PopCount((uint)mask) < 2)
            text += " " + Loc.T("Con un solo botón se activaría jugando: usa al menos dos.");
        if ((mask & ControllerListener.GuideMask) != 0)
            text += " " + Loc.T("La Guía la usa Steam (Big Picture y su overlay): mejor evitarla.");

        var current = Selected?.Name;
        var clash = _store.LoadAll().FirstOrDefault(p => p.Name != current && p.ControllerCombo == mask)?.Name;
        if (clash != null) text += " " + Loc.T("Ya la usa «{0}».", clash);
        ControllerHint.Text = text;
    }

    private async void ControllerRecord_Click(object sender, RoutedEventArgs e)
    {
        ControllerRecord.IsEnabled = false;
        Status(Loc.T("Mantén pulsada la combinación en el mando…"));
        try
        {
            var mask = await _controller.RecordAsync().WaitAsync(TimeSpan.FromSeconds(20));
            SetCombo(mask);
            Status(Loc.T("Combinación grabada: {0}. Pulsa «Guardar cambios».", ControllerListener.Describe(mask)), NoticeKind.Success);
        }
        catch (TimeoutException)
        {
            Status(Loc.T("No se detectó ningún mando. Comprueba que está conectado."), NoticeKind.Warning);
        }
        finally
        {
            ControllerRecord.IsEnabled = true;
        }
    }

    // --- Collapsible advanced groups -----------------------------------------------------
    // Each group is one row (icon, name, a one-line summary of what is set, chevron) that opens its settings below it.

    private void GroupHeader_Click(object sender, RoutedEventArgs e)
    {
        var header = (System.Windows.Controls.Primitives.ToggleButton)sender;
        // Mouse clicks get the short ease; keyboard activation is instant, because keys are pressed far too often to wait on motion.
        SetGroupOpen((string)header.Tag, header.IsChecked == true, animate: header.IsMouseOver);
    }

    private void SetGroupOpen(string name, bool open, bool animate = false)
    {
        if (FindName(name + "Header") is System.Windows.Controls.Primitives.ToggleButton header) header.IsChecked = open;
        if (FindName(name + "Body") is FrameworkElement body)
        {
            var wasOpen = body.Visibility == Visibility.Visible;
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open && !wasOpen && animate) Motion.Reveal(body, 6); // opens with an ease-out; closing is immediate
        }

        if (FindName(name + "Chevron") is SymbolIcon chevron)
        {
            var angle = open ? 180.0 : 0.0;
            var rotate = chevron.RenderTransform as RotateTransform;
            if (rotate == null || rotate.IsFrozen) chevron.RenderTransform = rotate = new RotateTransform(angle);
            if (animate && Motion.Enabled)
                rotate.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(angle, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new System.Windows.Media.Animation.ExponentialEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Exponent = 5 },
                });
            else
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                rotate.Angle = angle;
            }
        }
    }

    private void Summary_Changed(object sender, RoutedEventArgs e) => UpdateGroupSummaries();

    /// <summary>What a collapsed group tells you without opening it.</summary>
    private void UpdateGroupSummaries()
    {
        if (TvSummary == null || LaunchSummary == null) return; // events can fire while the window is still being built
        var tv = new List<string>();
        if (TvPowerCombo.SelectedIndex > 0) tv.Add(TvPowerOptions[TvPowerCombo.SelectedIndex].Label);
        if (TvInputCombo.SelectedIndex > 0) tv.Add(TvInputOptions[TvInputCombo.SelectedIndex].Label);
        TvSummary.Text = tv.Count == 0 ? Loc.T("Sin cambios en la tele") : string.Join(" · ", tv);

        var actions = LaunchBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        LaunchSummary.Text = actions switch { 0 => Loc.T("Ninguna"), 1 => Loc.T("1 acción"), _ => Loc.T("{0} acciones", actions) };
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        new HelpWindow(_settings.EmergencyHotkey) { Owner = this }.ShowDialog();

    private async void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_revertingSelection) return;
        var next = ProfileList.SelectedItem as ProfileItem;
        if (_dirty && _current != null && next?.Name != _current.Name)
        {
            // stay on the profile being edited until the person decides what to do with the changes
            var wanted = next?.Name;
            _revertingSelection = true;
            ProfileList.SelectedItem = ((IEnumerable<ProfileItem>)ProfileList.ItemsSource).FirstOrDefault(i => i.Name == _current.Name);
            _revertingSelection = false;
            if (!await ConfirmLeaveAsync()) return;
            ProfileList.SelectedItem = ((IEnumerable<ProfileItem>)ProfileList.ItemsSource).FirstOrDefault(i => i.Name == wanted);
            return;
        }
        ShowDetails();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NewName.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            Status(Loc.T("Escribe un nombre válido para el perfil."), NoticeKind.Warning);
            return;
        }
        // "Create" must never silently overwrite the screens of a profile that already has this name
        if (_store.List().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Status(Loc.T("Ya existe un perfil con ese nombre."), NoticeKind.Warning);
            return;
        }
        if (!await ConfirmLeaveAsync()) return; // the new profile takes over the screen
        Run(() =>
        {
            _store.SaveCurrentMonitors(name);
            NewName.Text = "";
            Reload(name);
            Status(Loc.T("Perfil '{0}' creado con los monitores actuales.", name), NoticeKind.Success);
        });
    }

    private async void Design_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeaveAsync()) return;
        var window = new LayoutDesignerWindow(_store) { Owner = this };
        if (window.ShowDialog() != true || window.SavedName is not { } name) return;
        Reload(name);
        Status(Loc.T("Perfil '{0}' guardado.", name), NoticeKind.Success);
    }

    private void EditScreens_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (_dirty) Save_Click(sender, e); // keep what was typed in the other fields; the editor reloads the profile
        var window = new LayoutDesignerWindow(_store, p.Name) { Owner = this };
        if (window.ShowDialog() != true) return;
        Reload(p.Name);
        Status(Loc.T("Perfil '{0}' guardado.", p.Name), NoticeKind.Success);
    }

    private void Recapture_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (_dirty) Save_Click(sender, e); // keep what was edited in the other fields
        Run(() =>
        {
            _store.SaveCurrentMonitors(p.Name);
            Reload(p.Name);
            Status(Loc.T("Monitores de '{0}' actualizados con la disposición actual.", p.Name), NoticeKind.Success);
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        Run(() =>
        {
            ApplyUi(p);
            _store.Save(p);
            Reload(p.Name);
            Status(Loc.T("Perfil '{0}' guardado.", p.Name), NoticeKind.Success);
        });
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty) Save_Click(sender, e); // what is on screen is what gets applied
        if (Selected is { } p) await ApplyProfile(p);
    }

    private async Task ApplyProfile(Profile p)
    {
        if (_applying) { Status(Loc.T("Ya se está aplicando un perfil; espera a que termine."), NoticeKind.Warning); return; }
        _applying = true;
        ApplyButton.IsEnabled = false;
        ApplyButton.Content = Loc.T("Aplicando…");
        try { await ApplyProfileCore(p); }
        finally
        {
            _applying = false;
            ApplyButton.IsEnabled = true;
            ApplyButton.Content = Loc.T("Aplicar perfil");
        }
    }

    private async Task ApplyProfileCore(Profile p)
    {
        try
        {
            Status(Loc.T("Aplicando '{0}'...", p.Name));
            var applied = await ProfileRunner.Run(p, line => Status(line), default,
                () => ConfirmLayoutWindow.Ask(_settings.ConfirmSeconds), _settings.ConfirmLayout);
            if (!applied)
            {
                Status(Loc.T("Disposición de '{0}' revertida porque no se confirmó.", p.Name), NoticeKind.Warning);
                return;
            }
            _active = p.Name;
            TrackScreens(); // this is the layout the person chose now
            Status(Loc.T("Perfil '{0}' aplicado.", p.Name), NoticeKind.Success);
            RebuildTrayMenu(_store.LoadAll().Select(p => new ProfileItem(p)));
        }
        catch (Exception ex)
        {
            Status(Loc.T("Error: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Eliminar perfil"),
            Content = Loc.T("¿Eliminar el perfil '{0}'? No se puede deshacer.", p.Name),
            PrimaryButtonText = Loc.T("Eliminar"),
            PrimaryButtonAppearance = ControlAppearance.Danger,
            CloseButtonText = Loc.T("Cancelar"),
        };
        if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        Run(() =>
        {
            _store.Delete(p.Name);
            _dirty = false; // the profile is gone: nothing left to save
            Reload();
            Status(Loc.T("Perfil '{0}' eliminado.", p.Name), NoticeKind.Success);
        });
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Status(Loc.T("Error: {0}", ex.Message), NoticeKind.Error); }
    }

    private readonly List<(DateTime Time, NoticeKind Kind, string Text)> _history = [];

    /// <summary>Shows a result in the footer, keeps it in the history and, when the window is not in front, as a notice on screen.</summary>
    private void Status(string text, NoticeKind kind = NoticeKind.Info) => Dispatcher.Invoke(() => Notify(text, kind));

    private void Notify(string text, NoticeKind kind)
    {
        StatusText.Text = text;
        (StatusIcon.Symbol, var tint) = kind switch
        {
            NoticeKind.Success => (SymbolRegular.CheckmarkCircle24, Color.FromRgb(0x30, 0xD1, 0x58)),
            NoticeKind.Warning => (SymbolRegular.Warning24, Color.FromRgb(0xFF, 0x9F, 0x0A)),
            NoticeKind.Error => (SymbolRegular.ErrorCircle24, Color.FromRgb(0xFF, 0x45, 0x3A)),
            _ => (SymbolRegular.Info24, Color.FromRgb(0x0A, 0x84, 0xFF)),
        };
        StatusIcon.Foreground = new SolidColorBrush(tint);

        _history.Insert(0, (DateTime.Now, kind, text));
        if (_history.Count > 30) _history.RemoveAt(_history.Count - 1);

        // A shortcut, the controller or a rule can run with the window closed: there is nobody looking at the footer.
        var inFront = IsVisible && WindowState != WindowState.Minimized && IsActive;
        if (!inFront || kind is NoticeKind.Error) ToastWindow.Show(text, kind);
    }

    // --- Theme ---------------------------------------------------------------------------

    /// <summary>"system" tracks Windows' light/dark mode live; "light" and "dark" pin it.</summary>
    private void ApplyTheme()
    {
        switch (_settings.Theme)
        {
            case "light":
                if (IsLoaded) SystemThemeWatcher.UnWatch(this); // nothing to stop yet while the window is still being built
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case "dark":
                if (IsLoaded) SystemThemeWatcher.UnWatch(this); // nothing to stop yet while the window is still being built
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                SystemThemeWatcher.Watch(this);
                break;
        }
        UpdateThemeButton();
    }

    private void UpdateThemeButton()
    {
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        // the icon shows what a click switches to
        ThemeButton.Icon = new SymbolIcon { Symbol = dark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24 };
        ThemeButton.ToolTip = dark ? Loc.T("Cambiar a tema claro") : Loc.T("Cambiar a tema oscuro");
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? "light" : "dark";
        _settings.Save();
        ApplyTheme();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => BuildSettingsMenu().IsOpen = true;

    /// <summary>
    /// Grouped by topic with a label per group, one check mark in the icon column showing what is selected now,
    /// and the live Windows value as quiet right-hand text instead of a parenthesis in the label.
    /// </summary>
    private ContextMenu BuildSettingsMenu()
    {
        var menu = new ContextMenu { PlacementTarget = SettingsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, MinWidth = 300 };

        void Section(string title)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = new System.Windows.Controls.TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Opacity = 0.7 },
                IsHitTestVisible = false,
                Focusable = false,
            });
        }

        // the icon column always exists, so the labels stay aligned whether or not a row is the current choice
        object Mark(bool on)
        {
            if (!on) return new Border { Width = 16, Height = 16 };
            var check = new SymbolIcon { Symbol = SymbolRegular.Checkmark24, FontSize = 16 };
            check.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentFillColorDefaultBrush");
            return check;
        }

        System.Windows.Controls.MenuItem Choice(string label, bool selected, Action pick, string? hint = null)
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, Icon = Mark(selected), InputGestureText = hint ?? "" };
            item.Click += (_, _) => pick();
            return item;
        }

        Section(Loc.T("Animaciones"));
        var windows = System.Windows.SystemParameters.ClientAreaAnimation ? Loc.T("activadas") : Loc.T("desactivadas");
        menu.Items.Add(Choice(Loc.T("Seguir a Windows"), _settings.Animations == "system", () => SetAnimations("system"), Loc.T("ahora {0}", windows)));
        menu.Items.Add(Choice(Loc.T("Siempre activadas"), _settings.Animations == "on", () => SetAnimations("on")));
        menu.Items.Add(Choice(Loc.T("Desactivadas"), _settings.Animations == "off", () => SetAnimations("off")));

        Section(Loc.T("Apariencia"));
        menu.Items.Add(Choice(Loc.T("Seguir al sistema"), _settings.Theme == "system", () => SetTheme("system")));
        menu.Items.Add(Choice(Loc.T("Claro"), _settings.Theme == "light", () => SetTheme("light")));
        menu.Items.Add(Choice(Loc.T("Oscuro"), _settings.Theme == "dark", () => SetTheme("dark")));

        var current = _settings.Language == "system" ? Loc.FromWindows() : _settings.Language;
        // Not a hover submenu: crossing the other rows on the way to it would close it. Clicking "Idioma" swaps this menu for the
        // language list, opened in the same place.
        var languages = new System.Windows.Controls.MenuItem
        {
            Header = Loc.T("Idioma"), Icon = Mark(false),
            InputGestureText = Loc.Languages.First(l => l.Code == current).Native + "  ›",
        };
        languages.Click += (_, _) => Dispatcher.BeginInvoke(() => BuildLanguageMenu(current).IsOpen = true);
        menu.Items.Add(languages);

        Section(Loc.T("Confirmar cambios de pantallas"));
        menu.Items.Add(Choice(Loc.T("Preguntar siempre"), _settings.ConfirmLayout == "always", () => SetConfirmLayout("always")));
        menu.Items.Add(Choice(Loc.T("Solo si puedo quedarme sin imagen"), _settings.ConfirmLayout == "risky", () => SetConfirmLayout("risky"), Loc.T("recomendado")));
        menu.Items.Add(Choice(Loc.T("No preguntar nunca"), _settings.ConfirmLayout == "never", () => SetConfirmLayout("never")));

        Section(Loc.T("Si Windows cambia tus pantallas"));
        menu.Items.Add(Choice(Loc.T("Restaurar mi disposición si desaparece una pantalla"), _settings.RestoreScreens, () => SetRestoreScreens(!_settings.RestoreScreens)));

        Section(Loc.T("Sistema"));
        menu.Items.Add(Choice(Loc.T("Iniciar con Windows"), Autostart.IsEnabled, ToggleAutostart));
        if (AppInfo.KofiUrl != "" || AppInfo.GitHubRepo != "")
        {
            menu.Items.Add(new Separator());
            if (AppInfo.KofiUrl != "")
            {
                var kofi = new System.Windows.Controls.MenuItem { Header = Loc.T("Invitar a un café en Ko-fi"), Icon = BrandIcons.Kofi() };
                kofi.Click += (_, _) => OpenLink(AppInfo.KofiUrl);
                menu.Items.Add(kofi);
            }
            if (AppInfo.GitHubRepo != "")
            {
                var git = new System.Windows.Controls.MenuItem { Header = Loc.T("Código en GitHub"), Icon = BrandIcons.GitHub() };
                git.Click += (_, _) => OpenLink(AppInfo.RepoUrl);
                menu.Items.Add(git);
            }
        }
        return menu;
    }

    private ContextMenu BuildLanguageMenu(string current)
    {
        var menu = new ContextMenu { PlacementTarget = SettingsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, MinWidth = 260 };

        System.Windows.Controls.MenuItem Row(string label, bool selected, string code, string? hint = null)
        {
            object mark = new Border { Width = 16, Height = 16 };
            if (selected)
            {
                var check = new SymbolIcon { Symbol = SymbolRegular.Checkmark24, FontSize = 16 };
                check.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentFillColorDefaultBrush");
                mark = check;
            }
            var item = new System.Windows.Controls.MenuItem { Header = label, Icon = mark, InputGestureText = hint ?? "" };
            item.Click += async (_, _) => { if (await ConfirmLeaveAsync()) SwitchLanguage(code); };
            return item;
        }

        menu.Items.Add(Row(Loc.T("Seguir a Windows"), _settings.Language == "system", "system", Loc.Languages.First(l => l.Code == Loc.FromWindows()).Native));
        foreach (var (code, native) in Loc.Languages)
            menu.Items.Add(Row(native, _settings.Language == code, code));
        return menu;
    }

    private void SetConfirmLayout(string mode)
    {
        _settings.ConfirmLayout = mode;
        _settings.Save();
    }

    private void SetAnimations(string mode)
    {
        _settings.Animations = mode;
        _settings.Save();
        Motion.Mode = mode;
        // show the effect right away: replay the entrance of the profile that is open
        _shownProfile = null;
        ShowDetails();
    }

    /// <summary>Saves the language and rebuilds the window, so every text (including the ones fixed in XAML) is read again.</summary>
    internal void SwitchLanguage(string setting)
    {
        _settings.Language = setting;
        _settings.Save();
        Loc.Use(setting);

        // Everything stays as it is (tray icon, shortcuts, rules, controller): only the texts are read again.
        var open = _shownProfile;
        LocTree.Translate(this);
        InitControllerUi();
        SetUpFooterText();
        Reload(Selected?.Name);
        _shownProfile = open;
        if (_update != null) UpdateButton.Content = Loc.T("Actualizar a {0}", _update.VersionText);
        // the last message in the footer is still in the old language
        Status(Loc.T("Listo."), NoticeKind.Info);
    }

    private void SetTheme(string theme)
    {
        _settings.Theme = theme;
        _settings.Save();
        ApplyTheme();
    }

    private void ToggleAutostart()
    {
        try
        {
            if (Autostart.IsEnabled) Autostart.Disable(); else Autostart.Enable();
            Status(Autostart.IsEnabled ? Loc.T("La app arrancará con Windows, en la bandeja.") : Loc.T("La app ya no arranca con Windows."), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            Status(Loc.T("No se pudo cambiar el inicio con Windows: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = HistoryButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, MaxWidth = 520 };
        if (_history.Count == 0)
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = Loc.T("Todavía no hay mensajes."), IsHitTestVisible = false, Focusable = false });

        foreach (var (time, kind, text) in _history.Take(15))
        {
            var line = new StackPanel { Margin = new Thickness(2, 2, 2, 2) };
            line.Children.Add(new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
            line.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{time:HH:mm:ss} · {kind switch { NoticeKind.Error => Loc.T("error"), NoticeKind.Warning => Loc.T("aviso"), NoticeKind.Success => Loc.T("hecho"), _ => Loc.T("info") }}",
                FontSize = 11, Opacity = 0.7,
            });
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = line, IsHitTestVisible = false, Focusable = false });
        }
        menu.IsOpen = true;
    }

    /// <summary>Puts the last confirmed monitor layout back; bound to a global hotkey for when the screen is unusable.</summary>
    private void EmergencyRestore()
    {
        try
        {
            ConfirmLayoutWindow.CancelPending();
            if (LastKnownGood.Restore())
            {
                TrackScreens();
                Status(Loc.T("Pantallas restauradas a la última disposición confirmada."), NoticeKind.Success);
            }
            else Status(Loc.T("Todavía no hay una disposición confirmada que restaurar."), NoticeKind.Warning);
        }
        catch (Exception ex)
        {
            Status(Loc.T("No se pudieron restaurar las pantallas: {0}", ex.Message), NoticeKind.Error);
        }
    }

    // --- Safety net: Windows rearranging the screens by itself -----------------------------

    private DisplayConfig.Layout? _intended;
    private HashSet<string> _connected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _monitorNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _guardTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private DateTime _guardQuietUntil;
    private readonly Queue<DateTime> _restores = new();

    private void StartScreenGuard()
    {
        TrackScreens();
        // Unplugging or powering off a monitor fires several changes in a row: wait until they stop before looking.
        _guardTimer.Tick += (_, _) => { _guardTimer.Stop(); CheckScreens(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => { _guardTimer.Stop(); _guardTimer.Start(); });
    }

    /// <summary>Remembers which monitors are connected and the layout in use: what the safety net compares against.</summary>
    private void TrackScreens()
    {
        try
        {
            _intended = DisplayConfig.Capture();
            RememberConnected(DisplayGuard.ConnectedPaths());
            // testing aid: pretend one more monitor is connected, so the next change looks like it disappeared
            if (Environment.GetEnvironmentVariable("SSS_FAKE_MONITOR") is { Length: > 0 } fake) _connected.Add(fake);
            _guardQuietUntil = DateTime.Now.AddSeconds(6); // the events caused by our own change are not Windows' doing
        }
        catch { /* no display information: the net simply stays idle */ }
    }

    private void RememberConnected(HashSet<string> connected)
    {
        _connected = connected;
        foreach (var m in LayoutBuilder.Available()) _monitorNames[m.Target.DevicePath] = m.Name;
    }

    private void CheckScreens()
    {
        try
        {
            if (_applying || DateTime.Now < _guardQuietUntil) { _guardTimer.Start(); return; }

            var before = _connected;
            var after = DisplayGuard.ConnectedPaths();
            var now = DisplayConfig.Capture();

            if (_settings.RestoreScreens && _intended != null && DisplayGuard.Plan(_intended, now, before, after) is { } plan)
            {
                while (_restores.Count > 0 && DateTime.Now - _restores.Peek() > TimeSpan.FromMinutes(1)) _restores.Dequeue();
                if (_restores.Count >= 2)
                {
                    // Something keeps changing them back: fighting it would make the screens flash, so step aside for a while.
                    _guardQuietUntil = DateTime.Now.AddMinutes(5);
                    _restores.Clear();
                    Status(Loc.T("Windows sigue cambiando tus pantallas. Pauso unos minutos."), NoticeKind.Warning);
                    _connected = after;
                    return;
                }

                _restores.Enqueue(DateTime.Now);
                DisplayConfig.Validate(plan);
                DisplayConfig.Apply(plan);
                _guardQuietUntil = DateTime.Now.AddSeconds(8);
                var gone = before.Where(p => !after.Contains(p)).Select(p => _monitorNames.GetValueOrDefault(p, Loc.T("una pantalla")));
                Status(Loc.T("Se desconectó «{0}»: dejé tus pantallas como estaban.", string.Join(", ", gone)), NoticeKind.Info);
                _connected = after;
                return;
            }

            // Nothing to put back. If no monitor disappeared, this layout is somebody's choice (Win+P, Display settings, a monitor
            // switched on on purpose): it becomes the one to keep.
            if (!before.Any(p => !after.Contains(p))) _intended = now;
            RememberConnected(after);
        }
        catch (Exception ex)
        {
            Status(Loc.T("No se pudieron restaurar las pantallas: {0}", ex.Message), NoticeKind.Warning);
        }
    }

    private void SetRestoreScreens(bool on)
    {
        _settings.RestoreScreens = on;
        _settings.Save();
        if (on) TrackScreens();
    }

    private void ShowSafetyNoticeOnce()
    {
        if (_settings.SafetyNoticeSeen) return;
        SafetyText.Text = Loc.T("Si un perfil puede dejarte sin imagen, verás una cuenta atrás. Sin confirmar, vuelve la disposición anterior. Si no ves nada, pulsa {0}.", _settings.EmergencyHotkey);
        SafetyBanner.Visibility = Visibility.Visible;
        Motion.Reveal(SafetyBanner, -6);
    }

    private void SafetyOk_Click(object sender, RoutedEventArgs e)
    {
        SafetyBanner.Visibility = Visibility.Collapsed;
        _settings.SafetyNoticeSeen = true;
        _settings.Save();
    }

    // --- Tray -------------------------------------------------------------

    private void RebuildTrayMenu(IEnumerable<ProfileItem> items)
    {
        System.Windows.Controls.MenuItem Item(string header, object icon, Action onClick, string? gesture = null)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header, Icon = icon, InputGestureText = gesture ?? "" };
            item.Click += (_, _) => onClick();
            return item;
        }
        System.Windows.Controls.MenuItem SymbolItem(string header, SymbolRegular symbol, Action onClick, string? gesture = null) =>
            Item(header, new SymbolIcon { Symbol = symbol }, onClick, gesture);

        var menu = new ContextMenu { MinWidth = 260 };

        var title = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
        title.Children.Add(new System.Windows.Controls.TextBlock { Text = AppInfo.Name, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = _active == null ? Loc.T("{0} · sin perfil aplicado", AppInfo.VersionText) : Loc.T("{0} · activo: {1}", AppInfo.VersionText, _active),
            FontSize = 11,
            Opacity = 0.65,
        });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = title, IsHitTestVisible = false, Focusable = false });
        menu.Items.Add(new Separator());

        var profiles = items.Where(i => !i.Name.StartsWith('_')).ToList();
        foreach (var item in profiles)
        {
            var profile = item.Profile;
            var symbol = profile.Name == _active ? SymbolRegular.CheckmarkCircle24 : item.Symbol;
            menu.Items.Add(SymbolItem(profile.Name, symbol, async () => await ApplyProfile(profile), profile.Hotkey));
        }
        if (profiles.Count > 0) menu.Items.Add(new Separator());

        if (_settings.AudioCycle.Outputs.Count > 1)
            menu.Items.Add(SymbolItem(Loc.T("Cambiar de salida de sonido"), SymbolRegular.Speaker224, CycleAudio, _settings.AudioCycle.Hotkey));
        menu.Items.Add(SymbolItem(Loc.T("Abrir ventana"), SymbolRegular.Open24, ShowWindow));
        if (_update != null)
            menu.Items.Add(SymbolItem(Loc.T("Actualizar a {0}", _update.VersionText), SymbolRegular.ArrowDownload24, () => _ = InstallUpdate()));
        else if (AppInfo.GitHubRepo != "")
            menu.Items.Add(SymbolItem(Loc.T("Buscar actualizaciones"), SymbolRegular.ArrowSync24, () => _ = CheckForUpdates(manual: true)));

        if (AppInfo.KofiUrl != "" || AppInfo.GitHubRepo != "") menu.Items.Add(new Separator());
        if (AppInfo.KofiUrl != "") menu.Items.Add(Item(Loc.T("Invitar a un café en Ko-fi"), BrandIcons.Kofi(), () => OpenLink(AppInfo.KofiUrl)));
        if (AppInfo.GitHubRepo != "") menu.Items.Add(Item(Loc.T("Código en GitHub"), BrandIcons.GitHub(), () => OpenLink(AppInfo.RepoUrl)));

        menu.Items.Add(new Separator());
        menu.Items.Add(SymbolItem(Loc.T("Restaurar pantallas"), SymbolRegular.ArrowUndo24, EmergencyRestore, _settings.EmergencyHotkey));
        menu.Items.Add(SymbolItem(Loc.T("Salir"), SymbolRegular.Power24, async () =>
            {
                ShowWindow(); // the question needs a window to ask in
                if (!await ConfirmLeaveAsync()) return;
                _reallyClosing = true;
                Close();
                Application.Current.Shutdown();
            }));
        TrayIcon.Menu = menu;
    }

    // --- Links and updates ------------------------------------------------------

    private static void EnsureLastKnownGood()
    {
        // The layout in use when the app starts is the best guess of one that works.
        try { if (LastKnownGood.Load() == null) LastKnownGood.Save(DisplayConfig.Capture()); }
        catch { /* no display info available; the emergency hotkey will say there is nothing to restore */ }
    }

    /// <summary>The emergency restore as a real button, with its shortcut drawn as keys so it can be read at a glance.</summary>
    private void SetUpFooterText()
    {
        EmergencyKeys.Children.Clear();
        foreach (var key in _settings.EmergencyHotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var label = new System.Windows.Controls.TextBlock { Text = key, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            var cap = new Border { Child = label, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 0) };
            cap.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            cap.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
            EmergencyKeys.Children.Add(cap);
        }
    }

    private void Emergency_Click(object sender, RoutedEventArgs e) => EmergencyRestore();

    private void SetUpFooter()
    {
        SetUpFooterText();
        VersionText.Text = AppInfo.VersionText;
        if (AppInfo.GitHubRepo != "") _ = CheckForUpdates(manual: false);
    }

    private static void OpenLink(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    private async Task CheckForUpdates(bool manual)
    {
        try
        {
            _update = await Updater.CheckAsync();
        }
        catch (Exception ex)
        {
            if (manual) Status(Loc.T("No se pudo buscar actualizaciones: {0}", ex.Message), NoticeKind.Error);
            return;
        }

        UpdateButton.Visibility = _update != null ? Visibility.Visible : Visibility.Collapsed;
        if (_update != null) UpdateButton.Content = Loc.T("Actualizar a {0}", _update.VersionText);
        if (manual) Status(_update != null ? Loc.T("Hay una versión nueva: {0}.", _update.VersionText) : Loc.T("Ya tienes la última versión."), _update != null ? NoticeKind.Success : NoticeKind.Info);
        RebuildTrayMenu(_store.LoadAll().Select(p => new ProfileItem(p)));
    }

    private void Update_Click(object sender, RoutedEventArgs e) => _ = InstallUpdate();

    private async Task InstallUpdate()
    {
        if (_update is not { } release) return;
        ShowWindow();
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Actualizar"),
            Content = Loc.T("Se descargará {0} desde GitHub y la app se reiniciará.", release.VersionText),
            PrimaryButtonText = Loc.T("Actualizar"),
            CloseButtonText = Loc.T("Ahora no"),
        };
        if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        if (!await ConfirmLeaveAsync()) return; // the app restarts: unsaved changes would be lost

        try
        {
            UpdateButton.IsEnabled = false;
            await Updater.DownloadAndInstallAsync(release, new Progress<string>(line => Status(line)));
            Status(Loc.T("Instalando la actualización..."));
            _reallyClosing = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            Status(Loc.T("No se pudo actualizar: {0}", ex.Message), NoticeKind.Error);
        }
    }

    private void Tray_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e) => ShowWindow();

    /// <summary>Development aid for snapshots: select a profile and choose the mode without saving anything.</summary>
    public void PreviewSelect(string? profile, bool advanced)
    {
        _settings.AdvancedMode = advanced;
        if (profile != null)
            ProfileList.SelectedItem = ((IEnumerable<ProfileItem>)ProfileList.ItemsSource).FirstOrDefault(i => i.Name == profile);
        ShowDetails();
    }

    /// <summary>Development aid: opens the tray menu off to the side and captures just its own window.</summary>
    public void SaveMenuSnapshot(string path, string which = "tray")
    {
        var menu = which == "settings" ? BuildSettingsMenu() : TrayIcon.Menu as ContextMenu;
        if (menu == null) return;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
        menu.HorizontalOffset = 300;
        menu.VerticalOffset = 200;
        menu.IsOpen = true;
        menu.UpdateLayout();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // Render the menu's own visual tree on the window's dark surface (the popup window itself is layered and captures black).
        var width = (int)Math.Ceiling(menu.ActualWidth);
        var height = (int)Math.Ceiling(menu.ActualHeight);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(menu), null, new Rect(0, 0, width, height));
        }
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var file = System.IO.File.Create(path)) encoder.Save(file);
        menu.IsOpen = false;
    }

    public void SaveSnapshot(string path)
    {
        UpdateLayout();
        var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    public void ShowFromOutside() => ShowWindow();

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Closes this window for good (not just hiding it to the tray) and stops what it started.</summary>
    public void CloseForReal()
    {
        Release();
        Close();
    }

    private bool _released;

    /// <summary>Stops everything this window registered (hotkeys, rules, controller, tray icon) so another window can take them over.</summary>
    public void Release()
    {
        _reallyClosing = true;
        if (_released) return;
        _released = true;
        _rulesEngine?.Dispose();
        _hotkeys?.Dispose();
        _controller.Dispose();
        TrayIcon.Dispose();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && _dirty)
        {
            e.Handled = true;
            Save_Click(this, new RoutedEventArgs());
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private static BitmapSource BuildIcon() => IconArt.Render(64);
}
