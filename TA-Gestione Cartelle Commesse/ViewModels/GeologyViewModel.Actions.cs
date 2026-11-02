using System.IO;
using System.Diagnostics;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class GeologyViewModel
{
    private readonly Func<GeologicalGeocodeQuery, CancellationToken, Task<GeocodingResult>>? geocode;
    public bool CanGeocode => geocode != null && CanLocate;
    public Command OpenFolderCommand { get; }
    public Task<GeocodingResult> FindPositionAsync(GeologicalGeocodeQuery query, CancellationToken token) =>
        geocode != null ? geocode(query, token) : throw new InvalidOperationException("Il server non è collegato alla ricerca geografica.");
    // L'archivio è trattato come file fisico: Explorer non riceve percorsi interni ZIP/7z.
    public static ProcessStartInfo FolderStartInfo(GeologicalReport report)
    {
        var path = ClientPaths.ForExplorer(report.PhysicalPath);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('"') || path.Any(char.IsControl)) throw new IOException("Percorso del documento non valido.");
        return new("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true };
    }
    private void OpenSelectedFolder()
    {
        if (Selected == null) return;
        try { Process.Start(FolderStartInfo(Selected)); Status = "Aperta la cartella che contiene " + (Selected.ArchiveEntry == null ? "il documento." : "l'archivio. Percorso interno: " + Selected.ArchiveEntry); }
        catch (Exception ex) { Status = "Cartella non aperta: " + ex.Message; }
    }
}
