using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Views;

public partial class GeologyMapView : UserControl, IDisposable
{
    public const string Origin = "https://maps.ta-commesse.example";
    private GeologicalReport[] rows = [];
    private string? selection;
    private bool ready, starting, disposed;
    public event Action<string>? ReportSelected;
    public event Action<double, double>? PositionPicked;
    public GeologyMapView() { InitializeComponent(); Loaded += async (_, _) => await StartAsync(); }
    public void SetReports(IEnumerable<GeologicalReport> reports) { rows = reports.Where(r => r.HasPosition).ToArray(); Send("rows", rows.Select(r => new { id = r.Id, title = r.Title, code = r.ProjectLabel, lat = r.Latitude, lng = r.Longitude })); }
    public void Select(string? id) { selection = id; Send("selection", id); }
    private void Send(string type, object? data) { if (ready && !disposed) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, data })); }
    private async Task StartAsync()
    {
        if (starting || disposed || ready || (AppContext.TryGetSwitch("TA.Tests.DisableMapNetwork", out var disabled) && disabled)) return;
        starting = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tassinari", "CommesseClient", "MapBrowser"));
            if (disposed) return;
            await Browser.EnsureCoreWebView2Async(environment); if (disposed) return;
            var core = Browser.CoreWebView2;
            core.Settings.UserAgent += " TA-Commesse/" + typeof(GeologyMapView).Assembly.GetName().Version?.ToString(3) + " (+https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse)";
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.SetVirtualHostNameToFolderMapping("maps.ta-commesse.example", Path.Combine(AppContext.BaseDirectory, "Assets", "Maps"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (e.Uri != Origin + "/index.html") e.Cancel = true; };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https" && (uri.Host == "www.openstreetmap.org" || uri.Host == "leafletjs.com"))
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception) { MapStatus.Text = "Impossibile aprire il browser predefinito."; }
            };
            core.WebMessageReceived += (_, e) =>
            {
                if (e.Source != Origin + "/index.html" || disposed) return;
                try
                {
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
                    switch (type.GetString())
                    {
                        case "ready": ready = true; Placeholder.Visibility = Visibility.Collapsed; SetReports(rows); Select(selection); break;
                        case "selected":
                            if (json.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && rows.Any(r => r.Id == id.GetString())) ReportSelected?.Invoke(id.GetString()!);
                            break;
                        case "position":
                            if (selection != null && json.RootElement.TryGetProperty("lat", out var lat) && json.RootElement.TryGetProperty("lng", out var lng) && lat.ValueKind == JsonValueKind.Number && lng.ValueKind == JsonValueKind.Number && lat.TryGetDouble(out var a) && lng.TryGetDouble(out var b) && a is >= -90 and <= 90 && b is >= -180 and <= 180) PositionPicked?.Invoke(a, b);
                            break;
                        case "tiles-error": MapStatus.Text = "Cartografia non disponibile. Elenco e posizioni restano consultabili; verifica la connessione Internet."; break;
                        case "tiles-loaded": MapStatus.Text = "Clic sui simboli: selezione. Clic destro sulla mappa: proponi una posizione per la relazione selezionata."; break;
                    }
                }
                catch (JsonException) { /* I messaggi della pagina non sono comandi generici. */ }
            };
            Browser.Source = new Uri(Origin + "/index.html");
        }
        catch (Exception) { Placeholder.Visibility = Visibility.Visible; PlaceholderText.Text = "Mappa non disponibile. Verifica Microsoft Edge WebView2 Runtime. L'elenco rimane utilizzabile."; }
        finally { starting = false; }
    }
    public void Dispose() { disposed = true; ready = false; Browser.Dispose(); }
}
