using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Models;

// Mantiene un indice per categoria/radice e JSON annuali, coordinati con lock sulla condivisione.
namespace TA_Gestione_Cartelle_Commesse.Services;

public static class SharedArchiveService
{
    // Percorso condiviso esplicitamente indicato dall'utente.
    public const string DefaultFolder = @"\\ingtassinari.it\data\amministrazione\_GESTIONE COMMESSE";
    // JSON leggibile senza dipendenze o database da installare.
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    // Normalizza i percorsi senza risolvere collegamenti o accedere alla rete.
    public static string Normalize(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
    // L'hash separa radici diverse anche quando la categoria è la stessa.
    public static string FilePath(string folder, string root, char prefix) => Path.Combine(folder,
        $"{prefix}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(root))))}.json");

    // Il JSON generale e un indice; i dati vengono salvati per anno senza cambiare la scansione.
    public static ArchiveSnapshot? Load(string folder, string root, char prefix)
        => AnnualArchive.Load(FilePath(folder, root, prefix), root, prefix);
    public static bool Save(string folder, ArchiveSnapshot data, CancellationToken token)
        => AnnualArchive.Save(FilePath(folder, data.Root, data.Prefix), data, token);
}