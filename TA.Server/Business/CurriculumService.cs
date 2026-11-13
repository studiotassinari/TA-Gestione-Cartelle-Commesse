using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.Business;

// Curriculum dello studio nello stesso archivio SQLite del gestionale: revisioni, registro delle modifiche e backup sono condivisi.
// Le fotografie sono file del servizio in una cartella propria; le cartelle delle commesse non vengono mai toccate.
public sealed class CurriculumService
{
    public const int MaxPhotoBytes = 8 * 1024 * 1024, MaxPhotos = 30;
    private const string Kind = "curriculum";
    private readonly BusinessStore store; private readonly Func<WorkProject[]> projects; private readonly string photoFolder; private readonly Func<string> template, graphics;
    public CurriculumService(BusinessService business, ServerSettings settings)
        : this(business.Store, business.Projects, PhotoFolder(settings), () => settings.CurriculumTemplate, () => settings.CurriculumGraphics is { Length: > 0 } configured ? configured : BundledGraphics) { }
    // Grafica fornita con il servizio: vale finché in Configura non viene indicata un'altra cartella.
    public static string BundledGraphics => Path.Combine(AppContext.BaseDirectory, "Assets", "CurriculumGrafica");
    public CurriculumService(BusinessStore store, Func<WorkProject[]> projects, string photoFolder, Func<string>? template = null, Func<string>? graphics = null)
    { this.store = store; this.projects = projects; this.photoFolder = Path.GetFullPath(photoFolder); this.template = template ?? (() => ""); this.graphics = graphics ?? (() => ""); }
    // Nomi fissi: il servizio non legge altro dalla cartella della grafica.
    public static readonly string[] GraphicNames = ["stile.json", "logo.png", "logo-chiaro.png", "copertina.jpg", "copertina.png", "strutture.png", "architettura.png", "impianti.png", "consulenza.png"];
    public const int MaxGraphicBytes = 2 * 1024 * 1024;
    public static string PhotoFolder(ServerSettings settings) => Path.Combine(settings.InventoryFolder, "curriculum-foto");

    // Tutti gli utenti autorizzati al programma possono consultare e aggiornare il curriculum; ogni modifica resta nel registro con l'autore.
    public object Execute(CurriculumCommand command, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException("Identità Windows mancante.");
        return store.Locked(() => Handle(command, actor));
    }
    private object Handle(CurriculumCommand c, string actor)
    {
        switch (c.Action)
        {
            case "Snapshot":
                return new CurriculumSnapshot(store.All<CurriculumWork>(Kind).OrderByDescending(w => w.End ?? w.Start ?? DateOnly.MinValue).ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                    projects().Select(p => new CurriculumProject(p.Id, p.Code, p.Category, p.Code + " — " + string.Join(" — ", new[] { p.SourceCustomerName, p.Subject }.Where(s => !string.IsNullOrWhiteSpace(s))))).ToArray(),
                    TemplatePath() != null, GraphicsFolder() != null);
            case "Save":
            {
                var input = Validate(c.Work ?? throw new ArgumentException("Lavoro mancante."));
                var id = input.Id.Length == 0 ? Guid.NewGuid().ToString("N") : Identifier(input.Id);
                var existing = store.Find<CurriculumWork>(Kind, id);
                // Le fotografie si aggiungono e si tolgono con i comandi dedicati: qui si aggiornano solo didascalie e ordine.
                var known = (existing?.Photos ?? []).ToDictionary(p => p.Id);
                var photos = (input.Photos ?? []).Where(p => known.ContainsKey(p.Id)).DistinctBy(p => p.Id).Select(p => known[p.Id] with { Caption = Limit(p.Caption, 300, "Didascalia") }).ToList();
                photos.AddRange(known.Values.Where(p => photos.All(x => x.Id != p.Id)));
                return Store(input with { Id = id, Photos = photos.ToArray() }, input.Revision, actor);
            }
            case "Delete":
            {
                var id = Identifier(c.Id);
                store.Delete(Kind, id, c.Revision, actor);
                var folder = Path.Combine(photoFolder, id);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                return true;
            }
            case "AddPhoto":
            {
                var work = Get(c.Id);
                var image = c.Image ?? throw new ArgumentException("Fotografia mancante.");
                if (image.Length is 0 or > MaxPhotoBytes) throw new ArgumentException("Fotografia vuota o più grande di 8 MB.");
                var type = image.Length > 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF ? "jpg"
                    : image.Length > 8 && image[0] == 0x89 && image[1] == 0x50 && image[2] == 0x4E && image[3] == 0x47 ? "png"
                    : throw new ArgumentException("Formato non ammesso: usare fotografie JPEG o PNG.");
                if ((work.Photos?.Length ?? 0) >= MaxPhotos) throw new ArgumentException("Limite di 30 fotografie per lavoro raggiunto.");
                var photo = new CurriculumPhoto(Guid.NewGuid().ToString("N"), Limit(Path.GetFileName(c.FileName ?? "fotografia"), 120, "Nome del file"), type, Limit(c.Caption, 300, "Didascalia"));
                var folder = Path.Combine(photoFolder, work.Id); Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, photo.Id + "." + type); var temporary = target + ".tmp";
                File.WriteAllBytes(temporary, image); File.Move(temporary, target, false);
                try { return Store(work with { Photos = (work.Photos ?? []).Append(photo).ToArray() }, c.Revision, actor); }
                catch { File.Delete(target); throw; }
            }
            case "DeletePhoto":
            {
                var work = Get(c.Id); var photo = Photo(work, c.PhotoId);
                var saved = Store(work with { Photos = work.Photos!.Where(p => p.Id != photo.Id).ToArray() }, c.Revision, actor);
                var file = Path.Combine(photoFolder, work.Id, photo.Id + "." + photo.Type);
                if (File.Exists(file)) File.Delete(file);
                return saved;
            }
            case "Photo":
            {
                var work = Get(c.Id); var photo = Photo(work, c.PhotoId);
                var file = Path.Combine(photoFolder, work.Id, photo.Id + "." + photo.Type);
                if (!File.Exists(file)) throw new FileNotFoundException("Fotografia non più disponibile sul server.");
                return File.ReadAllBytes(file);
            }
            case "Template":
            {
                var path = TemplatePath() ?? throw new IOException("Modello Word del curriculum non configurato: indicarlo nel Gestore server, pulsante Configura.");
                var file = new FileInfo(path);
                if (!file.Exists) throw new FileNotFoundException("Modello Word del curriculum non trovato: " + path);
                if (file.Length > 20 * 1024 * 1024) throw new IOException("Modello Word del curriculum più grande di 20 MB.");
                // Sola lettura condivisa: il modello resta modificabile da chi lo cura.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytes = new byte[stream.Length]; stream.ReadExactly(bytes); return bytes;
            }
            case "Graphics":
            {
                var folder = GraphicsFolder() ?? throw new IOException("Cartella della grafica del curriculum non configurata o non trovata: indicarla nel Gestore server, pulsante Configura.");
                var files = new List<CurriculumGraphicFile>();
                foreach (var name in GraphicNames)
                {
                    var file = new FileInfo(Path.Combine(folder, name));
                    if (!file.Exists) continue;
                    if (file.Length > MaxGraphicBytes) throw new IOException("File della grafica più grande di 2 MB: " + name);
                    // Sola lettura condivisa: il materiale resta modificabile da chi lo cura.
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var bytes = new byte[stream.Length]; stream.ReadExactly(bytes); files.Add(new CurriculumGraphicFile(name, bytes));
                }
                return new CurriculumGraphics(files.ToArray());
            }
            default: throw new ArgumentException("Comando curriculum non valido.");
        }
    }
    private string? TemplatePath()
    {
        var path = (template() ?? "").Trim();
        return path.Length > 0 && Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".dotx" ? path : null;
    }
    private string? GraphicsFolder()
    {
        var path = (graphics() ?? "").Trim();
        return path.Length > 0 && Directory.Exists(path) ? path : null;
    }
    private CurriculumWork Store(CurriculumWork work, int expected, string actor)
    {
        var saved = work with { Revision = expected + 1, UpdatedBy = actor, UpdatedUtc = DateTime.UtcNow };
        store.Save(Kind, saved.Id, expected, saved, actor, "Curriculum"); return saved;
    }
    private CurriculumWork Get(string? id) => store.Find<CurriculumWork>(Kind, Identifier(id)) ?? throw new ArgumentException("Lavoro del curriculum non trovato: aggiornare l'elenco.");
    private static CurriculumPhoto Photo(CurriculumWork work, string? id)
    { var key = Identifier(id); return (work.Photos ?? []).FirstOrDefault(p => p.Id == key) ?? throw new ArgumentException("Fotografia non trovata: aggiornare la scheda."); }
    // Gli identificativi diventano nomi di cartelle e file: solo 32 cifre esadecimali generate dal server.
    private static string Identifier(string? id) => id != null && Regex.IsMatch(id, "^[0-9a-f]{32}$") ? id : throw new ArgumentException("Identificativo non valido.");
    private static string Limit(string? value, int max, string label)
    {
        value = (value ?? "").Trim();
        return value.Length <= max ? value : throw new ArgumentException($"{label}: massimo {max} caratteri.");
    }
    public static CurriculumWork Validate(CurriculumWork w)
    {
        var title = Limit(w.Title, 300, "Titolo"); if (title.Length == 0) throw new ArgumentException("Indicare il titolo del lavoro.");
        if (w.Start is { } start && w.End is { } end && end < start) throw new ArgumentException("La data di fine precede la data di inizio.");
        if (w.WorksAmount < 0 || w.FeeAmount < 0) throw new ArgumentException("Gli importi non possono essere negativi.");
        var categories = (w.Categories ?? []).Select(c => new CurriculumCategory(Limit(c.Code, 20, "Categoria").ToUpperInvariant(), c.Amount, Limit(c.Description, 300, "Descrizione della categoria"), Limit(c.Grade, 20, "Grado della categoria"))).ToArray();
        if (categories.Length > 30 || categories.Any(c => c.Code.Length == 0 || c.Amount < 0)) throw new ArgumentException("Categorie delle opere non valide: codice obbligatorio, importo non negativo, al massimo 30 righe.");
        var keywords = (w.Keywords ?? []).Select(k => Limit(k, 60, "Parola chiave")).Where(k => k.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (keywords.Length > 40) throw new ArgumentException("Al massimo 40 parole chiave.");
        var codes = (w.ProjectCodes ?? []).Select(code => (code ?? "").Trim().ToUpperInvariant()).Where(code => code.Length > 0).Distinct().ToArray();
        if (codes.Length > 100 || codes.Any(code => !Regex.IsMatch(code, "^[CG][0-9]{5}$"))) throw new ArgumentException("Codici delle commesse collegate non validi: usare la forma C26001 o G26001.");
        // Le macro categorie arrivano in qualunque forma e ordine: restano solo quelle previste, nell'ordine del simbolo.
        var asked = (w.Sectors ?? []).Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).ToArray();
        if (asked.Any(s => !CurriculumSectors.All.Contains(s, StringComparer.OrdinalIgnoreCase))) throw new ArgumentException("Macro categoria non prevista: usare " + string.Join(", ", CurriculumSectors.All) + ".");
        var sectors = CurriculumSectors.All.Where(s => asked.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();
        return w with
        {
            Id = (w.Id ?? "").Trim(), Title = title, Customer = Limit(w.Customer, 300, "Committente"), Place = Limit(w.Place, 300, "Luogo"), Description = Limit(w.Description, 8000, "Descrizione"),
            Services = Limit(w.Services, 2000, "Prestazioni"), Role = Limit(w.Role, 300, "Ruolo"), Reference = Limit(w.Reference, 1000, "Riferimenti"),
            Categories = categories, Keywords = keywords, ProjectCodes = codes, Sectors = sectors
        };
    }
    // Copia nel backup le fotografie non ancora presenti; non elimina nulla dal backup.
    public static int MirrorPhotos(string source, string target)
    {
        if (!Directory.Exists(source)) return 0;
        int copied = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, false); copied++;
        }
        return copied;
    }
}
