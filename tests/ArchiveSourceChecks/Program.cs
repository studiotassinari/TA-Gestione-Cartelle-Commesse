using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using TA_Gestione_Cartelle_Commesse.Services;
static void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS: " + text); }
var root = Path.Combine(Path.GetTempPath(), "TA-ZipChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (var extension in new[] { "zip", "7z" })
    {
        var file = Path.Combine(root, "C26198 - Cliente - Progetto." + extension);
        if (extension == "zip")
        {
            using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("99_AMMINISTRAZIONE/Preventivo.pdf").Open()); writer.Write("test");
        }
        else
        {
            using var output = File.Create(file);
            using var writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip, new SevenZipWriterOptions(CompressionType.LZMA2));
            using var input = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("test"));
            writer.Write("99_AMMINISTRAZIONE/Preventivo.pdf", input, DateTime.UtcNow);
        }
        var before = SHA256.HashData(File.ReadAllBytes(file)); var modified = File.GetLastWriteTimeUtc(file);
        var result = ArchivedJobsReader.Inspect(file, default, true);
        Check(result.Complete && result.FileCount == 1 && result.TotalBytes == 4, extension + ": dimensione interna e conteggio letti dai metadati");
        Check(result.QuoteStatus.StartsWith("Presente") && result.ArchiveDirectories.Contains("99_AMMINISTRAZIONE"), extension + ": preventivo e gerarchia letti senza estrarre");
        Check(SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(before) && File.GetLastWriteTimeUtc(file) == modified && Directory.GetDirectories(root).Length == 0, extension + ": file invariato e nessuna estrazione");
    }
    File.WriteAllText(Path.Combine(root, "C26199 - guasto.7z"), "non e un archivio");
    var scan = ArchivedJobsReader.Scan(root, default);
    Check(scan.Records.Count == 3 && scan.Records.All(r => r.ArchiveBytes > 0), "Censimento include ZIP e 7z, con dimensioni compresse");
    Check(!ArchivedJobsReader.Inspect(Path.Combine(root, "C26199 - guasto.7z"), default).Complete, "Archivio danneggiato segnalato senza riparazioni");
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("TA-ZipChecks-")) Directory.Delete(root, true);
}
