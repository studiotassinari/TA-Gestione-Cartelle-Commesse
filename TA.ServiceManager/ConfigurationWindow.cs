using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace TA.ServiceManager;

// Prima configurazione e modifiche successive dell'installazione: dominio, gruppi e utenti restano
// nel file del server (ProgramData), mai nel codice. Le altre impostazioni già presenti sono conservate.
public sealed class ConfigurationWindow : Window
{
    private readonly TextBox domain = Line(), groups = Lines(), collaborators = Line(), managers = Lines(), port = Line(),
        jobs = Line(), bids = Line(), archived = Line(), data = Line(), template = Line(), bidTemplate = Line(), curriculumTemplate = Line(), analysisStart = Line(), analysisEnd = Line(), analysisRate = Line();
    private readonly CheckBox analysis = new() { Content = "Analizza automaticamente i documenti candidati su questo server (senza ChatGPT e senza servizi esterni)", Margin = new Thickness(0, 12, 0, 8) };
    private readonly CheckBox firewall = new() { Content = "Apri la porta nel firewall di Windows (solo profilo di dominio e sottorete locale)", IsChecked = true, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 8) };
    private readonly string serverFolder;
    public bool Saved { get; private set; }

    public ConfigurationWindow(string serverFolder)
    {
        this.serverFolder = serverFolder;
        Title = "Configurazione del server"; Width = 720; Height = 760; MinWidth = 560; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            Text = "Questi dati identificano l'installazione e vengono salvati solo su questo server, in " + ConfigurationFile.Path + ". Le modifiche valgono dal successivo avvio del servizio: dopo il salvataggio viene proposto il riavvio." });
        Add(panel, "Dominio Active Directory (per esempio azienda.example; vuoto = dominio di questo server)", domain);
        Add(panel, "Gruppi autorizzati a usare il programma — uno per riga, obbligatorio", groups);
        Add(panel, "Gruppo collaboratori per attività e ore — facoltativo", collaborators);
        Add(panel, "Utenti autorizzati alla gestione straordinaria (servizio, registri, archiviazione ZIP, gestionale) — uno per riga, utente@dominio oppure DOMINIO\\utente", managers);
        Add(panel, "Porta TCP dedicata", port);
        Add(panel, "Cartella delle commesse (percorso UNC)", jobs);
        Add(panel, "Cartella delle gare (percorso UNC)", bids);
        Add(panel, "Cartella dell'archivio (percorso UNC)", archived);
        Add(panel, "Cartella dati del servizio", data);
        Add(panel, "Cartella modello per le nuove commesse (vuoto = modello predefinito del servizio)", template);
        Add(panel, "Cartella modello per le nuove gare (vuoto = viene creata solo la cartella principale)", bidTemplate);
        Add(panel, "Modello Word per l'esportazione del curriculum, con intestazione e stili dello studio (.docx o .dotx; vuoto = impaginazione neutra)", curriculumTemplate);
        panel.Children.Add(analysis);
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "Limiti all'uso di risorse: un documento alla volta a priorità ridotta, al massimo 20 secondi e 512 MB per documento, in pausa durante le scansioni e le letture su richiesta. Qui sotto la fascia oraria e il numero di documenti l'ora." });
        Add(panel, "Ora di inizio dell'analisi automatica (0–23, ora italiana)", analysisStart);
        Add(panel, "Ora di fine (0–23; uguale all'inizio = tutto il giorno)", analysisEnd);
        Add(panel, "Documenti l'ora al massimo (1–120)", analysisRate);
        panel.Children.Add(firewall); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annulla", IsCancel = true, Margin = new Thickness(6), Padding = new Thickness(12, 7, 12, 7) };
        var save = new Button { Content = "Verifica e salva", Margin = new Thickness(6), Padding = new Thickness(12, 7, 12, 7) };
        save.Click += (_, _) => { try { Save(); } catch (Exception ex) { error.Text = "Salvataggio non riuscito: " + ex.Message; } };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        try { Fill(ConfigurationFile.Load()); }
        catch (Exception ex) { error.Text = "Configurazione esistente non leggibile: " + ex.Message + " Salvando verrà sostituita."; Fill(new ConfigurationFile()); }
    }
    private static TextBox Line() => new() { Margin = new Thickness(0, 4, 0, 12) };
    private static TextBox Lines() => new() { Margin = new Thickness(0, 4, 0, 12), AcceptsReturn = true, MinLines = 3, MaxLines = 8, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static void Add(Panel panel, string label, TextBox box)
    { panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(box); }
    private void Fill(ConfigurationFile value)
    {
        domain.Text = value.Domain; groups.Text = string.Join(Environment.NewLine, value.Groups); collaborators.Text = value.CollaboratorsGroup;
        managers.Text = string.Join(Environment.NewLine, value.ManagementUsers); port.Text = value.Port.ToString();
        analysis.IsChecked = value.LocalAnalysis; analysisStart.Text = value.LocalAnalysisStartHour.ToString(); analysisEnd.Text = value.LocalAnalysisEndHour.ToString(); analysisRate.Text = value.LocalAnalysisDocumentsPerHour.ToString();
        jobs.Text = value.JobsRoot; bids.Text = value.BidsRoot; archived.Text = value.ArchivedJobsRoot; data.Text = value.ArchiveFolder; template.Text = value.JobTemplate; bidTemplate.Text = value.BidTemplate; curriculumTemplate.Text = value.CurriculumTemplate;
    }
    private void Save()
    {
        error.Text = "";
        // Un valore non numerico diventa -1 e viene segnalato dalla verifica.
        static int Whole(TextBox box) => int.TryParse(box.Text.Trim(), out var parsed) ? parsed : -1;
        var value = new ConfigurationFile
        {
            LocalAnalysis = analysis.IsChecked == true, LocalAnalysisStartHour = Whole(analysisStart), LocalAnalysisEndHour = Whole(analysisEnd), LocalAnalysisDocumentsPerHour = Whole(analysisRate),
            Domain = domain.Text.Trim(), Groups = ConfigurationFile.Split(groups.Text), CollaboratorsGroup = collaborators.Text.Trim(),
            ManagementUsers = ConfigurationFile.Split(managers.Text), Port = int.TryParse(port.Text.Trim(), out var number) ? number : 0,
            JobsRoot = jobs.Text.Trim(), BidsRoot = bids.Text.Trim(), ArchivedJobsRoot = archived.Text.Trim(), ArchiveFolder = data.Text.Trim(), JobTemplate = template.Text.Trim(), BidTemplate = bidTemplate.Text.Trim(), CurriculumTemplate = curriculumTemplate.Text.Trim()
        };
        var problems = value.Validate();
        // Verifica l'esistenza dei nomi senza concedere o cambiare permessi nel dominio.
        foreach (var name in value.Groups.Append(value.CollaboratorsGroup).Concat(value.ManagementUsers).Where(n => n.Length > 0))
        {
            try { _ = new NTAccount(name).Translate(typeof(SecurityIdentifier)); }
            catch (IdentityNotMappedException) { problems.Add("Nome non trovato in Active Directory: " + name); }
            catch (SystemException ex) { problems.Add("Verifica di " + name + " non riuscita: " + ex.Message); }
        }
        if (problems.Count > 0) { error.Text = string.Join(Environment.NewLine, problems); return; }
        if (value.ManagementUsers.Length == 0 && MessageBox.Show(this, "Nessun utente è autorizzato alla gestione straordinaria: gestione del servizio dal client, archiviazione ZIP e amministrazione del gestionale resteranno disattivate. Salvare comunque?",
            "Gestione straordinaria", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        value.Save();
        // Permessi della cartella di configurazione e regola firewall restano nello script esistente.
        var script = Path.Combine(serverFolder, "Configure-Server.ps1");
        if (File.Exists(script))
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            foreach (var argument in new[] { "-NoProfile", "-NoExit", "-ExecutionPolicy", "Bypass", "-File", script, "-KeepSettings" }) start.ArgumentList.Add(argument);
            if (firewall.IsChecked == true) start.ArgumentList.Add("-OpenDomainFirewall");
            Process.Start(start);
        }
        Saved = true; DialogResult = true;
    }
}
