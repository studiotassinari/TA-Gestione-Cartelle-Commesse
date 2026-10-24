using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA.Transport;
using TA.ServiceManager;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.ViewModels;
internal static class Program
{
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    [STAThread] static void Main()
    {
        var app = new Application();
        foreach (var prefix in new[] { 'C', 'G' })
        {
            var vm = new CategoryViewModel(prefix == 'C' ? "Commesse" : "Gare", prefix);
            vm.Connect(new ServerConnection("test.invalid", 5443));
            var first = new FolderRecord(prefix + "26197", "26", "197", "Cliente", "Progetto", @"C:\test\" + prefix + "26197", "Valido");
            var next = new FolderRecord(prefix + "26198", "26", "198", "Da verificare", "prov prova", @"C:\test\" + prefix + "26198 - prov prova", "Non conforme");
            var now = DateTime.UtcNow;
            var snapshot = new ArchiveSnapshot { Records = [first], SavedUtc = now };
            var state = new CategoryState(prefix, @"C:\test", true, "Lettura metadati in corso", "", snapshot);
            var grid = new DataGrid { ItemsSource = vm.Records };
            int notified = 0;
            vm.DataChanged += message => { Check(vm.Records.Any(r => r.Code == next.Code), prefix + ": vista aggiornata prima dell'avviso"); notified++; };
            vm.ApplyState(state);
            Check(notified == 0 && grid.Items.Count == 1, prefix + ": prima fotografia carica la griglia senza falsi avvisi");
            vm.ApplyState(state with { Snapshot = snapshot with { Records = [first, next], SavedUtc = now.AddTicks(1) } });
            Check(notified == 1 && grid.Items.Count == 2 && vm.ChangesLog.Any(x => x.Contains("26198")), prefix + ": push aggiorna griglia WPF e cronologia durante scansione");
            vm.ApplyState(state with { Snapshot = snapshot with { Records = [first, next], SavedUtc = now.AddTicks(2) } });
            Check(notified == 1, prefix + ": fotografia invariata non ripete l'avviso");
        }
        var service = new ServiceLogView(); service.Output.Text = "17:10:00 Servizio avviato\n17:10:01 Elenco C verificato: 320 cartelle\n17:10:02 Metadati in corso; elenco disponibile";
        var communications = new CommunicationLogView();
        var events = new[] { new CommunicationRecord(DateTimeOffset.Now, "PC-UFFICIO", "192.0.2.10", @"DOMINIO\utente", "Changed", "Riscontro ricevuto", "Revisione=42; viste applicate dal client", "prova"), new CommunicationRecord(DateTimeOffset.Now, "PC-UFFICIO", "192.0.2.10", @"DOMINIO\utente", "Heartbeat", "Riscontro ricevuto", "Collegamento verificato", "prova2") };
        communications.Update(string.Join('\n', events.Select(e => System.Text.Json.JsonSerializer.Serialize(e))));
        var panel = new Grid { Width = 1200, Height = 700, Background = Brushes.White };
        panel.RowDefinitions.Add(new RowDefinition()); panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(service); Grid.SetRow(communications, 1); panel.Children.Add(communications);
        panel.Measure(new Size(1200, 700)); panel.Arrange(new Rect(0, 0, 1200, 700)); panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
        Directory.CreateDirectory("artifacts/checks"); using var file = File.Create("artifacts/checks/log-controls.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
        Check(true, "Due controlli WPF dei log renderizzati");
        app.Shutdown();
    }
}
