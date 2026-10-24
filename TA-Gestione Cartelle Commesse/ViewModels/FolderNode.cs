using System.Collections.ObjectModel;
using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

// Ogni espansione chiede al server soltanto i figli del nodo: non riconosce ulteriori commesse.
public sealed class FolderNode : Observable
{
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNode> Children { get; } = [];
    private readonly string id, relative;
    private readonly char category;
    private readonly ServerConnection? connection;
    private bool expanded, loaded;
    public bool IsExpanded { get => expanded; set { expanded = value; Notify(); if (value && !loaded) _ = Load(); } }
    public FolderNode(string path, string folderId, char prefix, ServerConnection server, string relativePath = "")
    {
        Path = path; Name = System.IO.Path.GetFileName(path); id = folderId; category = prefix; connection = server; relative = relativePath;
        Children.Add(new("Caricamento..."));
    }
    private FolderNode(string message) { Name = Path = message; id = relative = ""; loaded = true; }
    private async Task Load()
    {
        loaded = true;
        try
        {
            var children = await connection!.SendAsync<FolderEntry[]>(new("Children", category.ToString(), id, relative));
            Children.Clear();
            foreach (var child in children) Children.Add(new(child.Path, id, category, connection, child.RelativePath));
        }
        catch (Exception ex) { Children.Clear(); Children.Add(new("Errore: " + ex.Message)); loaded = false; }
    }
}
