using System.Collections.ObjectModel;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Ogni espansione chiede al server soltanto i figli del nodo: non riconosce ulteriori commesse.
public sealed class FolderNode : Observable
{
    public Command OpenCommand { get; }
    private readonly string? archivePath;
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNode> Children { get; } = [];
    private readonly string id, relative;
    private readonly char category;
    private readonly ServerConnection? connection;
    private bool expanded, loaded;
    public bool IsExpanded { get => expanded; set { expanded = value; Notify(); if (value && !loaded) _ = Load(); } }
    public FolderNode(string path, string folderId, char prefix, ServerConnection server, string relativePath = "", string? archive = null)
    {
        archivePath = archive ?? (prefix == 'A' && relativePath.Length == 0 ? path : null);
        OpenCommand = new(Open);
        Path = path; Name = System.IO.Path.GetFileName(path); id = folderId; category = prefix; connection = server; relative = relativePath;
        Children.Add(new("Caricamento..."));
    }
    private FolderNode(string message) { OpenCommand = new(() => {}, () => false); Name = Path = message; id = relative = ""; loaded = true; }
    private void Open()
    {
        try
        {
            var target = ClientPaths.ForExplorer(archivePath ?? Path);
            if (archivePath != null)
            {
                // Un ramo interno a un archivio non è una directory fisica: seleziona l'archivio senza estrarlo.
                var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add("/select,"); start.ArgumentList.Add(target); System.Diagnostics.Process.Start(start);
            }
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Apertura cartella non riuscita"); }
    }
    private async Task Load()
    {
        loaded = true;
        try
        {
            var children = await connection!.SendAsync<FolderEntry[]>(new("Children", category.ToString(), id, relative));
            Children.Clear();
            foreach (var child in children) Children.Add(new(child.Path, id, category, connection, child.RelativePath, archivePath));
        }
        catch (Exception ex) { Children.Clear(); Children.Add(new("Errore: " + ex.Message)); loaded = false; }
    }
}
