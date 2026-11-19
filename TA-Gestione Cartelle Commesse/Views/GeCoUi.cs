using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Elementi grafici comuni a Pianificazione e Ore lavorate, con i colori dello studio (#135F6E, #192845, fondo #F3F6F6) e
// l'aspetto delle pagine di GeCo2027. I pulsanti hanno un proprio modello: quello generale della finestra principale
// renderebbe illeggibili i pulsanti chiari al passaggio del mouse.
internal static class GeCoUi
{
    public static readonly Brush Teal = Hex("#135F6E"), Night = Hex("#192845"), Back = Hex("#F3F6F6"), Grey = Hex("#5B6678"), Rule = Hex("#D5DDE1"),
        Track = Hex("#F7F9FA"), TodayBack = Hex("#E4F0F2"), Selection = Hex("#D9730D"), Danger = Hex("#8F2A1C"), DangerBack = Hex("#F6E3DF"),
        Warning = Hex("#8A5A00"), WarningBack = Hex("#FBF0D9"), Neutral = Hex("#F3F5F6"), PersonBack = Hex("#EEF3F4"), Error = Hex("#B3261E"), ErrorBack = Hex("#FBE9E7");
    public static SolidColorBrush Hex(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    private static readonly ControlTemplate Template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.85"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#D9730D"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    public static Button Push(object content, Action? click = null, bool active = false, string? tip = null)
    {
        var b = new Button { Template = Template, Content = content, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 6), Cursor = System.Windows.Input.Cursors.Hand,
            Background = active ? Teal : Brushes.White, Foreground = active ? Brushes.White : Night, BorderBrush = active ? Teal : Hex("#C9D2D6"), BorderThickness = new Thickness(1),
            FontWeight = FontWeights.Normal, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
        if (click != null) b.Click += (_, _) => click();
        return b;
    }
    public static Button Link(string text, Action click, string? tip = null)
    {
        var b = Push(text, click, tip: tip); b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; b.Foreground = Teal; b.Padding = new Thickness(4, 2, 4, 2);
        b.Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline }; return b;
    }
    // Contenitore bianco con bordo, come i riquadri delle pagine di GeCo2027.
    public static Border Card(UIElement content, Thickness? padding = null) => new()
    { Background = Brushes.White, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = padding ?? new Thickness(12), Child = content };
    public static TextBlock Caption(string text, double size = 13, bool bold = false, Brush? color = null, bool wrap = true) => new()
    { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = color ?? Night, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    // Messaggio a tutta larghezza: avviso (giallo), errore (rosso), esito (verde) o informazione (grigio).
    public static Border Notice(string text, string kind = "warning")
    {
        var (back, fore) = kind switch { "danger" => (DangerBack, Danger), "success" => (Hex("#E3F1E6"), Hex("#1E5B2E")), "info" => (Neutral, Night), _ => (WarningBack, Warning) };
        return new Border { Background = back, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10), Child = Caption(text, 13, false, fore) };
    }
    // Trascinamento di una fase (o di una sagoma) dal pannello verso il giorno di una persona; il clic semplice resta un clic.
    public const string DragFormat = "TA.GeCo.Trascinamento";
    // Trascinamento in corso (DoDragDrop lascia lavorare il dispatcher): una ricarica per un aggiornamento esterno aspetta la fine.
    public static bool Dragging { get; private set; }
    // Avviso discreto «Aggiornato da …» in fondo alla finestra, che sparisce da solo dopo qualche secondo: non sposta la griglia
    // come farebbe un messaggio in testa e non chiede nulla all'utente.
    public static Border Refreshed(out Action<string> show)
    {
        var text = Caption("", 12.5, false, Night, false);
        var border = new Border { Background = Brushes.White, BorderBrush = Teal, BorderThickness = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 5, 12, 5), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Child = text };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(text, System.Windows.Automation.AutomationLiveSetting.Polite);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => { timer.Stop(); border.Visibility = Visibility.Collapsed; };
        show = message => { text.Text = "↻ " + message + " alle " + DateTime.Now.ToString("HH:mm:ss"); border.Visibility = Visibility.Visible; timer.Stop(); timer.Start(); };
        return border;
    }
    public static void DragSource(FrameworkElement element, object data)
    {
        Point? start = null;
        element.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(element);
        element.PreviewMouseMove += (_, e) =>
        {
            if (start is not { } s || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var p = e.GetPosition(element);
            if (Math.Abs(p.X - s.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - s.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null; Dragging = true;
            try { DragDrop.DoDragDrop(element, new DataObject(DragFormat, data), DragDropEffects.Copy); }
            finally { Dragging = false; }
        };
        element.PreviewMouseLeftButtonUp += (_, _) => start = null;
    }
    public static void Prepare(Window window, string title)
    {
        var main = Application.Current.MainWindow;
        window.Title = title; window.Width = 1400; window.Height = 900; window.MinWidth = 960; window.MinHeight = 600;
        window.Background = Back; window.Foreground = Night;
        if (main != null && !ReferenceEquals(main, window)) { window.FontFamily = main.FontFamily; window.Icon = main.Icon; window.Resources = main.Resources; }
        window.FontSize = 13;
    }
}
