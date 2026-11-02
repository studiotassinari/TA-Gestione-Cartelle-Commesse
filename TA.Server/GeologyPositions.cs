using System.Security.Cryptography;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

public sealed partial class GeologyService
{
    private string PositionsPath => Path.Combine(GeologyStorage.Root(settings), "posizioni.json");
    private (GeologyCatalog Catalog, string Version) MergePositions(GeologyCatalog catalog, byte[] bytes)
    {
        var positions = ReadPositions();
        var byId = positions.ToDictionary(p => p.ReportId, StringComparer.Ordinal);
        var reports = catalog.Reports.Select(r => byId.TryGetValue(r.Id, out var p) ? r with {
            Latitude = p.Latitude, Longitude = p.Longitude, Municipality = p.Municipality, Address = p.Address,
            PositionSource = p.Source, PositionAccuracy = "Indicata da " + p.UpdatedBy + " il " + p.UpdatedUtc.ToString("dd/MM/yyyy HH:mm 'UTC'")
        } : r).ToList();
        var version = Convert.ToHexString(SHA256.HashData(bytes.Concat(JsonSerializer.SerializeToUtf8Bytes(positions)).ToArray()));
        return (catalog with { Reports = reports }, version);
    }
    private List<GeologicalPosition> ReadPositions()
    {
        if (!File.Exists(PositionsPath)) return [];
        if (new FileInfo(PositionsPath).Length > 10 * 1024 * 1024) throw new IOException("Archivio delle posizioni troppo grande.");
        return InventoryStore.Read<GeologicalPositions>(PositionsPath).Positions;
    }
    public bool SavePosition(GeologicalPositionUpdate change, string user, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(change.Version) || string.IsNullOrWhiteSpace(change.ReportId) ||
            new[] { change.Municipality, change.Address, change.Source }.Any(s => s == null || s.Length > 500) || string.IsNullOrWhiteSpace(change.Source) ||
            change.Latitude is not (>= -90 and <= 90) || change.Longitude is not (>= -180 and <= 180)) throw new ArgumentException("Indicare coordinate valide, fonte della posizione e versione del catalogo.");
        Directory.CreateDirectory(GeologyStorage.Root(settings));
        using var writer = new FileStream(PositionsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var (catalog, version) = ReadCatalog(token);
        if (change.Version != version) throw new IOException("Catalogo cambiato: aggiorna l'elenco prima di salvare la posizione.");
        if (!catalog.Reports.Any(r => r.Id == change.ReportId)) throw new KeyNotFoundException("Relazione non presente nel catalogo.");
        var positions = ReadPositions().Where(p => p.ReportId != change.ReportId).ToList();
        positions.Add(new(change.ReportId, change.Latitude, change.Longitude, change.Municipality.Trim(), change.Address.Trim(), change.Source.Trim(), DateTime.UtcNow, user));
        token.ThrowIfCancellationRequested();
        GeologyStorage.Atomic(PositionsPath, new GeologicalPositions(positions)); return true;
    }
}
