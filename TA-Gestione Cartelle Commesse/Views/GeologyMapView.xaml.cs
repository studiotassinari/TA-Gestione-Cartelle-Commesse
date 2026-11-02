using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA_Gestione_Cartelle_Commesse.Views;

public partial class GeologyMapView : UserControl, IDisposable
{
    public const string Origin = "https://maps.ta-commesse.example";
    private GeologicalReport[] rows = [];
    private string? selection;
    private bool ready, starting, disposed;
    public event Action<string>? ReportSelected;
    public GeologyMapView() { InitializeComponent(); Loaded += async (_, _) => await StartAsync(); }
    public void SetReports(IEnumerable<GeologicalReport> reports) { rows = reports.Where(r => r.HasPosition).ToArray(); Send("rows", rows.Select(r => new { id = r.Id, title = r.Title, code = r.ProjectLabel, lat = r.Latitude, lng = r.Longitude })); }
    public void Select(string? id) { selection = id; Send("selection", id); }
    private void Send(string type, object? data) { if (ready && !disposed) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, data })); }
    private async Task StartAsync()
    {
        if (starting || disposed) return;
        starting = true;
        try
        {
            var settings = GoogleMapSettings.Load();
            if (!settings.Configured) { Placeholder.Visibility = Visibility.Visible; return; }
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "MapBrowser"));
            if (disposed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (disposed) return;
            var core = Browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.SetVirtualHostNameToFolderMapping("maps.ta-commesse.example", Path.Combine(AppContext.BaseDirectory, "Assets", "Maps"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (e.Uri != Origin + "/index.html") e.Cancel = true; };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                // Attribuzione e condizioni di Google aprono soltanto pagine HTTPS del fornitore.
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https" && (uri.Host == "google.com" || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)))
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception) { MapStatus.Text = "Impossibile aprire il browser predefinito."; }
            };
            core.WebMessageReceived += (_, e) =>
            {
                if (e.Source != Origin + "/index.html" || disposed) return;
                try
                {
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String) return;
                    var type = typeValue.GetString();
                    if (type == "ready") { ready = true; Send("init", new { apiKey = settings.ApiKey, mapId = settings.MapId }); SetReports(rows); Select(selection); }
                    if (type == "selected" && json.RootElement.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String) { var id = idValue.GetString(); if (id != null && rows.Any(r => r.Id == id)) ReportSelected?.Invoke(id); }
                    if (type == "loaded") { Placeholder.Visibility = Visibility.Collapsed; MapStatus.Text = "Seleziona un simbolo per vedere le relazioni in quella posizione."; }
                    if (type == "error") { Placeholder.Visibility = Visibility.Visible; PlaceholderText.Text = "Mappa non disponibile. Controlla connessione, chiave Google Maps e ID mappa."; }
                }
                catch (JsonException) { /* Ignora messaggi incompleti, senza eseguire comandi. */ }
            };
            Browser.Source = new Uri(Origin + "/index.html");
        }
        catch (Exception) { Placeholder.Visibility = Visibility.Visible; PlaceholderText.Text = "Impossibile avviare la mappa. Verifica che Microsoft Edge WebView2 Runtime sia installato e che la configurazione sia valida."; }
        finally { starting = false; }
    }
    private void Configure_Click(object sender, RoutedEventArgs e)
    {
        GoogleMapSettings current;
        try { current = GoogleMapSettings.Load(); } catch { current = new(); }
        var key = new PasswordBox { Password = current.ApiKey, Padding = new Thickness(8), Margin = new Thickness(0,4,0,12) };
        var mapId = new TextBox { Text = current.MapId, Padding = new Thickness(8), Margin = new Thickness(0,4,0,12) };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Google Maps Platform", FontSize = 21, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Richiede Maps JavaScript API e fatturazione abilitata nel progetto Google. Usa una chiave limitata a questa API e al sito https://maps.ta-commesse.example/*.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,14) });
        panel.Children.Add(new TextBlock { Text = "Chiave API" }); panel.Children.Add(key);
        panel.Children.Add(new TextBlock { Text = "ID mappa JavaScript" }); panel.Children.Add(mapId);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkRed };
        panel.Children.Add(error);
        var save = new Button { Content = "Salva", Padding = new Thickness(18,8,18,8), HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(save);
        var dialog = new Window { Title = "Configurazione Google Maps", Width = 510, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        save.Click += (_, _) => { try { new GoogleMapSettings(key.Password.Trim(), mapId.Text.Trim()).Save(); dialog.DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
        if (dialog.ShowDialog() == true) { MapStatus.Text = "Configurazione salvata. Chiudi e riapri questa finestra per applicarla."; }
    }
    public void Dispose() { disposed = true; ready = false; Browser.Dispose(); }
}
