using System.Windows;
using System.Windows.Controls;
using MonitorTv.Core;
using Wpf.Ui.Controls;

namespace MonitorTv.App;

/// <summary>Short, task-first help: what a profile is, how to apply it, and what to do when something goes wrong.</summary>
public sealed class HelpWindow : FluentWindow
{
    public HelpWindow(string emergencyHotkey)
    {
        Title = Loc.T("Ayuda");
        Width = 560;
        Height = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;

        var body = new StackPanel { Margin = new Thickness(28, 10, 28, 24) };
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = Loc.T("Cómo funciona"), FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });

        void Section(string title, string text)
        {
            body.Children.Add(new System.Windows.Controls.TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 4) });
            body.Children.Add(new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        }

        Section(Loc.T("Perfiles"),
            Loc.T("Un perfil guarda tus pantallas (cuáles y dónde) y la salida de sonido. Colócalo todo en Windows y pulsa «Crear con los monitores actuales»."));
        Section(Loc.T("Aplicarlo"),
            Loc.T("Desde la lista, la bandeja o su atajo (funciona con la ventana cerrada). Si el perfil puede dejarte sin imagen, verás una cuenta atrás: sin «Mantener», vuelve la disposición anterior."));
        Section(Loc.T("Si te quedas sin imagen"),
            Loc.T("No hagas nada: vuelve sola la disposición anterior. O pulsa {0} para restaurar ya.", emergencyHotkey));
        Section(Loc.T("Alternar sonido"),
            Loc.T("Un atajo que pasa a la siguiente salida de sonido, p. ej. de altavoces a auriculares."));
        Section(Loc.T("Modo avanzado"),
            Loc.T("Tele LG, acciones extra al aplicar, atajo de mando y reglas automáticas."));
        Section(Loc.T("Reglas automáticas"),
            Loc.T("Aplican un perfil o cambian el sonido al abrir o cerrar un programa. Ej.: Steam Big Picture → perfil TV; al cerrarlo, vuelve el anterior."));
        Section(Loc.T("Animaciones"),
            Loc.T("Sigue la opción «Efectos de animación» de Windows. Con el engranaje puedes forzarlas o desactivarlas."));
        Section(Loc.T("Datos"),
            Loc.T("Tus perfiles y ajustes se guardan en %AppData%\\ScreenSoundSwitcher."));

        var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Cerrar"), Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 24, 0, 0) };
        close.Click += (_, _) => Close();
        body.Children.Add(close);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = Loc.T("Ayuda"), ShowMaximize = false, ShowMinimize = false });
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }
}
