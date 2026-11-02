using System.Globalization;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;

namespace TA.Server;

// Classifica soltanto nomi già censiti. Non apre i file e non interpreta codici numerici come coordinate.
public static class GeologyClassifier
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".xls", ".xlsx" };
    private static readonly Regex Geology = new(@"\b(?:geologic\w*|geologi[ae]|geological)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Geotechnics = new(@"\b(?:geotecnic\w*|geotechnic\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Coordinates = new(@"\blat(?:itudine)?\s*[=:]?\s*(?<lat>[+-]?\d{1,2}[.,]\d{3,9})\s*[;_ ]+\s*lon(?:gitudine)?\s*[=:]?\s*(?<lon>[+-]?\d{1,3}[.,]\d{3,9})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static GeologicalReport? Classify(InventoryManifest manifest, InventoryEntry entry, string containerStamp = "", DateTime? containerModifiedUtc = null)
    {
        if (entry.Kind == "directory") return null;
        var location = entry.Location ?? FileLocator.Locate(manifest.Unit, entry);
        if (!Extensions.Contains(location.Extension) || location.Name.StartsWith("~$", StringComparison.Ordinal)) return null;
        static string Words(string value) => value.Replace('_', ' ').Replace('-', ' ');
        var name = Words(Path.GetFileNameWithoutExtension(location.Name));
        bool geology = Geology.IsMatch(name), geotechnics = Geotechnics.IsMatch(name);
        bool direct = geology || geotechnics;
        if (!direct)
        {
            // Un documento in una cartella tematica è un candidato, non una relazione confermata.
            var context = Words(location.ParentRelativePath + " " + Path.GetFileNameWithoutExtension(entry.Container ?? "") + " " + Path.GetFileName(manifest.Unit.Path));
            geology = Geology.IsMatch(context); geotechnics = Geotechnics.IsMatch(context);
            if (!geology && !geotechnics) return null;
        }
        var match = Coordinates.Match(entry.RelativePath.Replace('_', ' '));
        double? lat = null, lon = null;
        if (match.Success && double.TryParse(match.Groups["lat"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
            double.TryParse(match.Groups["lon"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
        { lat = latitude; lon = longitude; }
        var classified = manifest.Unit.Classification == "censita";
        // Per gli archivi, una modifica del contenitore invalida anche voci con data interna invariata.
        var stamp = string.Join('|', location.Id, entry.Bytes?.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.ModifiedUtc?.ToString("O"), containerStamp);
        if (entry.Bytes == null || entry.ModifiedUtc == null || location.InternalPath != null && containerStamp.Length == 0) stamp += "|" + manifest.Generation;
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp)));
        return new GeologicalReport {
            SourceMetadataComplete = entry.Bytes.HasValue && entry.ModifiedUtc.HasValue && (location.InternalPath == null || containerStamp.Length > 0 && !containerStamp.StartsWith("unknown:", StringComparison.Ordinal)),
            SourceFingerprint = fingerprint, SourceBytes = entry.Bytes, SourceModifiedUtc = containerModifiedUtc > entry.ModifiedUtc || entry.ModifiedUtc == null ? containerModifiedUtc : entry.ModifiedUtc,
            Id = location.Id, Title = Path.GetFileNameWithoutExtension(location.Name), FileName = location.Name,
            PhysicalPath = location.PhysicalPath, ArchiveEntry = location.InternalPath, Category = manifest.Unit.Category,
            FolderId = manifest.Unit.Id, RelativePath = entry.RelativePath, InventoryUnitId = manifest.Unit.Id,
            Year = manifest.Unit.Year, ProjectCode = classified ? manifest.Unit.Code : "", Client = manifest.Unit.Client,
            SuggestedProjectCode = classified ? "" : manifest.Unit.Code,
            Kind = geology && geotechnics ? "Geologica e geotecnica" : geology ? "Geologica" : "Geotecnica",
            DetectionEvidence = direct ? "Termine geologico/geotecnico nel nome del file" : "Documento in una cartella tematica: tipologia da verificare",
            DetectionStatus = "Da verificare", SourceCheckedUtc = manifest.CheckedUtc, SourceComplete = manifest.Complete,
            Latitude = lat, Longitude = lon, PositionSource = lat.HasValue ? "Etichette lat/lon nel nome censito: da verificare" : "",
            PositionAccuracy = lat.HasValue ? "Coordinate nel nome, non verificate" : "",
            Notes = "Individuazione dai nomi; contenuto non letto." + (classified ? "" : " Unità non classificata: nessuna commessa attribuita automaticamente.")
        };
    }
}
