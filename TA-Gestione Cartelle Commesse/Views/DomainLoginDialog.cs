using System.Net;
using System.Windows;
using System.Windows.Controls;
namespace TA_Gestione_Cartelle_Commesse.Views;

// Richiede credenziali solo per la sessione; la password non viene legata a proprietà testuali o salvata.
public sealed class DomainLoginDialog : Window
{
    public NetworkCredential? Credential { get; private set; }
    public DomainLoginDialog()
    {
        Title = "Accesso con account di dominio"; Width = 430; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Utente (DOMINIO\\utente oppure utente@dominio)" });
        var user = new TextBox { Margin = new Thickness(0, 6, 0, 16) }; panel.Children.Add(user);
        panel.Children.Add(new TextBlock { Text = "Password" });
        var password = new PasswordBox { Margin = new Thickness(0, 6, 0, 16) }; panel.Children.Add(password);
        panel.Children.Add(new TextBlock { Text = "Le credenziali restano solo in memoria fino alla chiusura o al cambio connessione.", TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annulla", IsCancel = true, Margin = new Thickness(6) };
        var login = new Button { Content = "Accedi", IsDefault = true, Margin = new Thickness(6) };
        buttons.Children.Add(cancel); buttons.Children.Add(login); panel.Children.Add(buttons); Content = panel;
        login.Click += (_, _) =>
        {
            var name = user.Text.Trim();
            using var secret = password.SecurePassword;
            if (secret.Length == 0 || (!name.Contains('\\') && !name.Contains('@'))) { error.Text = "Indicare utente di dominio e password."; return; }
            var parts = name.Split('\\', 2);
            if (parts.Length == 2 && (parts[0].Length == 0 || parts[1].Length == 0)) { error.Text = "Nome utente non valido."; return; }
            Credential = parts.Length == 2 ? new NetworkCredential(parts[1], secret, parts[0]) : new NetworkCredential(name, secret);
            password.Clear(); DialogResult = true;
        };
        Closed += (_, _) => password.Clear();
    }
}
