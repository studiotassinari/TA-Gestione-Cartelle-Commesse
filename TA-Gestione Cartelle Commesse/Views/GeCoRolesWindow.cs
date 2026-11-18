using System.Windows;
using System.Windows.Controls;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using static TA_Gestione_Cartelle_Commesse.Views.GeCoUi;
namespace TA_Gestione_Cartelle_Commesse.Views;

// «Ruoli e team» della pianificazione (components/RuoliTeam.jsx di GeCo2027): gli amministratori vedono chi è amministratore e
// danno o tolgono il ruolo di team manager. I team permanenti e i team per persona non hanno ancora regole nella libreria
// comune: per non duplicarle restano nella stessa finestra di GeCo2027.
internal sealed class GeCoRolesWindow : Window
{
    private readonly PlanningViewModel vm;
    private readonly StackPanel content = new() { Margin = new Thickness(18) };
    public GeCoRolesWindow(Window owner, PlanningViewModel model, bool administrator)
    {
        vm = model; Prepare(this, "Ruoli e team — TA"); Owner = owner; Width = 760; Height = 640; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel();
        var close = Push("Chiudi", Close); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(18, 8, 18, 12); close.IsCancel = true;
        DockPanel.SetDock(close, Dock.Bottom); dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
        if (administrator) Loaded += async (_, _) => await Load();
        else Inform("Come team manager vedi la pianificazione e le ore di tutte le persone senza poterle modificare.");
    }
    private void Inform(string text)
    {
        content.Children.Clear();
        content.Children.Add(Notice(text, "info"));
        content.Children.Add(Teams());
    }
    private static TextBlock Teams() => Caption("Team permanenti e team per persona (le commesse del team di ciascuno) si gestiscono per ora in GeCo, pagina Pianificazione > Ruoli e team: il consuntivo delle ore li usa già per proporre le commesse.", 13, false, Grey);
    private async Task Load()
    {
        TA_Gestione_Cartelle_Commesse.Contracts.GeCoRoles roles;
        try { roles = await vm.RolesAsync(); }
        catch (Exception ex) { Inform(PlanningViewModel.Message(ex)); return; }
        content.Children.Clear();
        content.Children.Add(Caption("Amministratori", 16, true, Teal));
        content.Children.Add(Caption("Vedono e pianificano tutti e gestiscono ruoli e team. Si diventa amministratori nella pagina Utenti di GeCo, con il tipo «Amministratore» oppure con «Tutte le funzioni».", 12, false, Grey));
        foreach (var u in roles.Amministratori)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var how = Caption(u.Utente + " · " + u.Come, 12, false, Grey, false); DockPanel.SetDock(how, Dock.Right); row.Children.Add(how);
            row.Children.Add(Caption(u.Nome, 13, false, null, false)); content.Children.Add(row);
        }
        content.Children.Add(Caption("Team manager", 16, true, Teal).With(t => t.Margin = new Thickness(0, 18, 0, 0)));
        content.Children.Add(Caption("Vedono la pianificazione e le ore di tutte le persone senza poterle modificare, e gestiscono i team.", 12, false, Grey));
        if (roles.Utenti.Length == 0) content.Children.Add(Caption("Nessun utente attivo non amministratore."));
        foreach (var u in roles.Utenti)
        {
            var id = u.Id;
            var box = new CheckBox { Content = u.Nome + " — team manager", IsChecked = u.TeamManager, Margin = new Thickness(0, 6, 0, 0) };
            box.Click += async (_, _) =>
            {
                try { await vm.SetTeamManagerAsync(id, box.IsChecked == true); }
                catch (Exception ex) { MessageBox.Show(this, PlanningViewModel.Message(ex), "Ruoli e team"); }
                await Load();
            };
            content.Children.Add(box);
        }
        content.Children.Add(Teams().With(t => t.Margin = new Thickness(0, 18, 0, 0)));
    }
}
