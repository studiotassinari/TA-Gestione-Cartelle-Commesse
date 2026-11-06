using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TA_Gestione_Cartelle_Commesse;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
using TA_Gestione_Cartelle_Commesse.Views;

internal static class Program
{
    [STAThread] static void Main()
    {
        var app = new Application(); var main = new MainWindow(); app.MainWindow = main;
        var day = DateOnly.FromDateTime(DateTime.Today);
        var snapshot = new BusinessSnapshot(@"INGTASSINARI\utente-prova", true,
            [new("cliente", "Cliente dimostrativo", "00000000000", Address: "Indirizzo di prova")],
            [new("p1", "C26001", "Progetto dimostrativo"), new("p2", "C26002", "Secondo progetto")],
            [new("a1", "p1", "Progettazione", "Preparazione elaborati", @"INGTASSINARI\utente-prova", day, day.AddDays(8), 1200, Progress: 35), new("a2", "p2", "Verifica", "Controllo documenti", @"INGTASSINARI\altro", day.AddDays(2), day.AddDays(5), 600)],
            [new("t1", "p1", "a1", day, 90, "Attività dimostrativa", @"INGTASSINARI\utente-prova")], [], "Backup dimostrativo");
        using var vm = new BusinessViewModel((cmd, ct) => Task.FromResult(JsonSerializer.SerializeToElement(snapshot)));
        var window = new BusinessWindow(vm); vm.Refresh().GetAwaiter().GetResult();
        var body = (FrameworkElement)window.Content;
        var tabs = Descendants<TabControl>(body).Single();
        if (tabs.Items.Count != 5) throw new Exception("Schede gestionale mancanti");
        foreach (var index in new[] { 0, 1, 2, 3, 4 })
        {
            tabs.SelectedIndex = index; body.Measure(new Size(1260, 760)); body.Arrange(new Rect(0, 0, 1260, 760)); body.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1260, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(body);
            Directory.CreateDirectory("artifacts/checks"); using var stream = File.Create($"artifacts/checks/gestionale-{index}.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        }
        if (!vm.CanEdit || snapshot.Times.Length != 1) throw new Exception("Dati non applicati");
        Console.WriteLine("BusinessUiChecks: caricamento senza rete e cinque schede WPF renderizzate."); app.Shutdown();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { if (child is T match) yield return match; foreach (var found in Descendants<T>(child)) yield return found; }
    }
}
