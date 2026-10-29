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
            Loc.T("Un perfil guarda cómo están tus pantallas (cuáles están encendidas y dónde) y por qué salida suena el sonido. Coloca todo en Windows como lo quieres y pulsa «Crear con los monitores actuales»."));
        Section(Loc.T("Aplicarlo"),
            Loc.T("Desde la lista con «Aplicar perfil», desde el icono de la bandeja o con su atajo de teclado, que funciona con la ventana cerrada. Si el perfil puede dejarte sin imagen, verás una cuenta atrás: si no pulsas «Mantener», vuelve la disposición anterior. Puedes cambiar cuándo se pregunta en el engranaje de ajustes."));
        Section(Loc.T("Si te quedas sin imagen"),
            Loc.T("No hagas nada unos segundos: la disposición anterior vuelve sola. También puedes pulsar {0} en cualquier momento para restaurar las pantallas.", emergencyHotkey));
        Section(Loc.T("Alternar sonido"),
            Loc.T("Un solo atajo que pasa de una salida de sonido a la siguiente, por ejemplo de los altavoces a los auriculares. Eliges qué salidas entran en el ciclo."));
        Section(Loc.T("Modo avanzado"),
            Loc.T("Añade la tele LG (encender, apagar, entrada), acciones extra al aplicar (programas, enlaces y llamadas web), un atajo de mando y las reglas automáticas."));
        Section(Loc.T("Reglas automáticas"),
            Loc.T("Aplican un perfil o cambian la salida de sonido cuando un programa se abre o se cierra. Por ejemplo, al abrir Steam Big Picture pasar al perfil de la tele, y al cerrarlo volver al anterior."));
        Section(Loc.T("Animaciones"),
            Loc.T("La app respeta la opción de Windows «Efectos de animación». Si la tienes desactivada, no verás movimiento. Con el engranaje de abajo a la derecha puedes elegir «Siempre activadas» o «Desactivadas»."));
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
