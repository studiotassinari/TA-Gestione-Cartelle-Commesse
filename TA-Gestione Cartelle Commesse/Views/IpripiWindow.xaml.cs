using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;

public partial class IpripiWindow : Window
{
    private readonly IpripiViewModel model;
    public IpripiWindow(IpripiViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        Loaded += async (_, _) => await model.LoadAsync();
        Closed += (_, _) => model.Dispose();
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(model.ChatRequest()); MessageBox.Show(this, "Richiesta copiata. Incollala in ChatGPT con il plugin TA – Gestione Commesse, poi aggiorna questa finestra.", "Analisi IPRiPI"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Richiesta non copiata"); }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "analisi-ipripi.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { Reference = "DGR 2272/2016, Allegato 1", ExportedUtc = DateTime.UtcNow, Documents = model.Rows }, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Esportazione non riuscita"); }
    }
}
