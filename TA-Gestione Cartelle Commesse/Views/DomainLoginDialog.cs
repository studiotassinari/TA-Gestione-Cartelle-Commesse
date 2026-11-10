using System.Net;
using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.Views;

// La password non viene legata a proprietà testuali; viene memorizzata solo se l'utente lo sceglie,
// e in quel caso esclusivamente in Gestione credenziali di Windows.
public sealed class DomainLoginDialog : Window
{
    public NetworkCredential? Credential { get; private set; }
    public string AccountName { get; private set; } = "";
    public bool Remember { get; private set; }
    public DomainLoginDialog(string accountName = "", bool remember = true, string message = "")
    {
        Title = "Accesso con account di dominio"; Width = 430; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        if (message.Length > 0) panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Utente (DOMINIO\\utente oppure utente@dominio)" });
        var user = new TextBox { Margin = new Thickness(0, 6, 0, 16), Text = accountName }; panel.Children.Add(user);
        panel.Children.Add(new TextBlock { Text = "Password" });
        var password = new PasswordBox { Margin = new Thickness(0, 6, 0, 16) }; panel.Children.Add(password);
        var keep = new CheckBox { Content = "Ricorda la password su questo computer", IsChecked = remember, Margin = new Thickness(0, 0, 0, 6) }; panel.Children.Add(keep);
        panel.Children.Add(new TextBlock { Text = "Il nome utente viene sempre ricordato. La password, se scelto, resta in Gestione credenziali di Windows del tuo profilo; altrimenti solo in memoria fino alla chiusura.", TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annulla", IsCancel = true, Margin = new Thickness(6) };
        var login = new Button { Content = "Accedi", IsDefault = true, Margin = new Thickness(6) };
        buttons.Children.Add(cancel); buttons.Children.Add(login); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { if (accountName.Length > 0) password.Focus(); else user.Focus(); };
        login.Click += (_, _) =>
        {
            var name = user.Text.Trim();
            using var secret = password.SecurePassword;
            if (secret.Length == 0 || (!name.Contains('\\') && !name.Contains('@'))) { error.Text = "Indicare utente di dominio e password."; return; }
            var parts = name.Split('\\', 2);
            if (parts.Length == 2 && (parts[0].Length == 0 || parts[1].Length == 0)) { error.Text = "Nome utente non valido."; return; }
            Credential = SavedCredential.Credential(name, secret); AccountName = name; Remember = keep.IsChecked == true;
            password.Clear(); DialogResult = true;
        };
        Closed += (_, _) => password.Clear();
    }
}
