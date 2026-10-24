using System.Collections.ObjectModel;
using System.IO;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Il client riceve fotografie dal server: non legge il JSON condiviso né scansiona la rete.
public sealed class CategoryViewModel : Observable
{
    private readonly char prefix;
    private ServerConnection? connection;
    private readonly SemaphoreSlim polling = new(1);
    private List<FolderRecord> all = [];
    private DateTime? snapshotTime;
    private string? dataFingerprint;
    public event Action<string>? DataChanged;
    private string rootPath = "Configurato sul server", filter = "", status = "Connettere il server.", archiveStatus = "";
    private bool busy, connected;
    private FolderRecord? selected;
    public string Title { get; }
    public string RootPath { get => rootPath; private set { rootPath = value; Notify(); } }
    public string Filter { get => filter; set { filter = value; Notify(); ApplyFilter(); } }
    public string Status { get => status; private set { status = value; Notify(); } }
    public string ArchiveStatus { get => archiveStatus; private set { archiveStatus = value; Notify(); } }
    public bool IsIdle => !busy;
    // Il coordinatore interrompe il polling dopo un errore di connessione.
    public bool ConnectionFailed { get; private set; }
    public Exception? LastConnectionError { get; private set; }
    public ObservableCollection<FolderRecord> Records { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<FolderNode> Hierarchy { get; } = [];
    public FolderRecord? Selected
    {
        get => selected;
        set
        {
            selected = value; Notify(); OpenFolderCommand.Refresh(); Hierarchy.Clear();
            if (value != null && connection != null) Hierarchy.Add(new(value.Path, value.Id, prefix, connection) { IsExpanded = true });
        }
    }
    public Command RefreshCommand { get; }
    public Command CancelCommand { get; }
    public Command OpenFolderCommand { get; }
    public CategoryViewModel(string title, char category)
    {
        Title = title; prefix = category;
        RefreshCommand = new(() => _ = SendCommand("Refresh"), () => connected && !busy);
        CancelCommand = new(() => _ = SendCommand("Cancel"), () => connected && busy);
        OpenFolderCommand = new(OpenFolder, () => Selected != null);
    }
    public void Connect(ServerConnection server)
    {
        dataFingerprint = null; ConnectionFailed = false; connection = server; snapshotTime = null; connected = false; busy = false;
        all = []; ApplyFilter(); Warnings.Clear(); UpdateCommands();
    }
    // Una sola richiesta di stato per categoria; mantiene la selezione se la fotografia non cambia.
    public async Task PollAsync()
    {
        if (connection == null) return;
        await polling.WaitAsync();
        var current = connection;
        try
        {
            var state = await current.SendAsync<CategoryState>(new("State", prefix.ToString()));
            if (!ReferenceEquals(current, connection)) return;
            LastConnectionError = null; ConnectionFailed = false; connected = true; busy = state.Busy; RootPath = state.Root; Status = state.Message;
            ArchiveStatus = state.ArchiveStatus;
            if (state.Snapshot is { } snapshot && snapshot.SavedUtc != snapshotTime)
            {
                // Ignora l'ora della scansione: notifica solo differenze nei dati effettivi.
                var fingerprint = System.Text.Json.JsonSerializer.Serialize(snapshot.Records.OrderBy(r => r.Path).Select(r => new
                {
                    r.Path, r.Code, r.Client, r.Subject, r.Status,
                    r.Inspection?.TotalBytes, r.Inspection?.FileCount, r.Inspection?.LastFileWriteUtc,
                    r.Inspection?.QuoteStatus, r.Inspection?.Complete, r.Inspection?.QuoteFiles, r.Inspection?.Issues
                }));
                if (dataFingerprint != null && dataFingerprint != fingerprint)
                    DataChanged?.Invoke("Il server ha rilevato modifiche. Apri l'applicazione per consultare l'elenco aggiornato.");
                dataFingerprint = fingerprint;
                snapshotTime = snapshot.SavedUtc; all = snapshot.Records; ApplyFilter(); Warnings.Clear();
                foreach (var warning in snapshot.Warnings) Warnings.Add(warning);
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(current, connection)) { LastConnectionError = ex; ConnectionFailed = true; connected = false; Status = "Server non disponibile; dati precedenti conservati: " + ex.Message; }
        }
        finally { UpdateCommands(); polling.Release(); }
    }
    public void Disconnected() { connected = false; ConnectionFailed = true; Status = "Connessione interrotta; dati precedenti conservati."; UpdateCommands(); }
    private async Task SendCommand(string operation)
    {
        if (connection == null) return;
        try { await connection.SendAsync<bool>(new(operation, prefix.ToString())); await PollAsync(); }
        catch (Exception ex) { Status = "Comando non riuscito: " + ex.Message; }
    }
    private void UpdateCommands() { Notify(nameof(IsIdle)); RefreshCommand.Refresh(); CancelCommand.Refresh(); }
    private void ApplyFilter()
    {
        var selectedId = Selected?.Id;
        Selected = null; Records.Clear();
        foreach (var row in all.Where(r => (r.Code + " " + r.Client + " " + r.Subject).Contains(filter, StringComparison.OrdinalIgnoreCase))) Records.Add(row);
        if (selectedId != null) Selected = Records.FirstOrDefault(row => row.Id == selectedId);
    }
    // L'apertura in Esplora file usa i permessi dell'utente sul percorso UNC, non quelli del server.
    private void OpenFolder()
    {
        if (Selected is not { } record) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(record.Path) { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Apertura cartella non riuscita"); }
    }
}
