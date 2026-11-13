using TA.Server.Business;
using TA_Gestione_Cartelle_Commesse.Contracts;

// Dati sintetici: nessun lavoro, committente o fotografia reale.
internal static class CurriculumChecks
{
    public static void Run(string folder)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return; } throw new Exception(message); }
        var projects = new[] { new WorkProject("p1", "C24031", "Scuola", Subject: "Adeguamento sismico", SourceCustomerName: "Comune di Esempio", Category: "C"), new WorkProject("p2", "C24031", "Scuola", Subject: "Adeguamento sismico", Category: "A") };
        var photoFolder = Path.Combine(folder, "foto"); var template = Path.Combine(folder, "modello.dotx"); string configured = "";
        var service = new CurriculumService(new BusinessStore(Path.Combine(folder, "dati")), () => projects, photoFolder, () => configured);
        T Do<T>(CurriculumCommand command, string actor = "utente.uno") => (T)service.Execute(command, actor);

        var empty = Do<CurriculumSnapshot>(new("Snapshot"));
        Check(empty.Works.Length == 0 && empty.Projects.Length == 2 && !empty.TemplateConfigured && empty.Projects[0].Label.Contains("Comune di Esempio"), "Curriculum iniziale non vuoto o commesse non elencate");
        var draft = new CurriculumWork("", "  Adeguamento sismico di una scuola  ", "Comune di Esempio", "Esempio (XX)", "Descrizione sintetica.", "Progettazione strutturale", "Incaricato",
            new DateOnly(2024, 3, 1), new DateOnly(2025, 6, 30), 1250000m, 85000m, [new("s.03", 900000m, "Strutture"), new("E.08", 350000m)], "RUP di prova",
            [" scuola ", "Scuola", "sismica"], ["c24031"]);
        var saved = Do<CurriculumWork>(new("Save", Work: draft));
        Check(saved.Id.Length == 32 && saved.Revision == 1 && saved.Title == "Adeguamento sismico di una scuola" && saved.UpdatedBy == "utente.uno", "Lavoro non salvato con identificativo, revisione e autore");
        Check(saved.Categories!.Select(c => c.Code).SequenceEqual(new[] { "S.03", "E.08" }) && saved.Keywords!.Length == 2 && saved.ProjectCodes!.Single() == "C24031", "Categorie, parole chiave o codici non normalizzati");
        Reject(() => Do<CurriculumWork>(new("Save", Work: saved with { Revision = 0, Title = "Vecchia revisione" })), "Revisione superata non rifiutata");
        Reject(() => Do<CurriculumWork>(new("Save", Work: draft with { Title = " " })), "Titolo vuoto accettato");
        Reject(() => Do<CurriculumWork>(new("Save", Work: draft with { ProjectCodes = ["..\\C24031"] })), "Codice commessa non valido accettato");
        Reject(() => Do<CurriculumWork>(new("Save", Work: draft with { End = new DateOnly(2020, 1, 1) })), "Fine precedente all'inizio accettata");
        Reject(() => Do<CurriculumWork>(new("Save", Work: draft with { WorksAmount = -1 })), "Importo negativo accettato");
        Reject(() => Do<CurriculumWork>(new("Save", Work: draft with { Id = "../altro" })), "Identificativo non valido accettato");
        // Tutti gli utenti autorizzati possono aggiornare: la modifica di un secondo utente resta attribuita a lui.
        var second = Do<CurriculumWork>(new("Save", Work: saved with { Description = "Aggiornata da un collega." }), "utente.due");
        Check(second.Revision == 2 && second.UpdatedBy == "utente.due", "Modifica di un secondo utente non registrata");

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        Reject(() => Do<CurriculumWork>(new("AddPhoto", Id: second.Id, Revision: second.Revision, Image: [1, 2, 3, 4, 5], FileName: "non-immagine.jpg")), "File non immagine accettato come fotografia");
        Reject(() => Do<CurriculumWork>(new("AddPhoto", Id: second.Id, Revision: 1, Image: jpeg, FileName: "foto.jpg")), "Fotografia aggiunta su revisione superata");
        Check(!Directory.Exists(Path.Combine(photoFolder, second.Id)) || Directory.GetFiles(Path.Combine(photoFolder, second.Id)).Length == 0, "Fotografia rifiutata rimasta su disco");
        var withPhoto = Do<CurriculumWork>(new("AddPhoto", Id: second.Id, Revision: second.Revision, Image: jpeg, FileName: @"C:\percorso\Facciata nord.JPG", Caption: "Facciata"));
        var photo = withPhoto.Photos!.Single();
        Check(photo.FileName == "Facciata nord.JPG" && photo.Type == "jpg" && withPhoto.Revision == 3 && File.Exists(Path.Combine(photoFolder, withPhoto.Id, photo.Id + ".jpg")), "Fotografia non salvata nella cartella del servizio");
        Check(Do<byte[]>(new("Photo", Id: withPhoto.Id, PhotoId: photo.Id)).SequenceEqual(jpeg), "Fotografia riletta diversa da quella inviata");
        Reject(() => Do<byte[]>(new("Photo", Id: withPhoto.Id, PhotoId: "../" + photo.Id)), "Identificativo fotografia non valido accettato");
        // Il salvataggio della scheda aggiorna le didascalie ma non può togliere o inventare fotografie.
        var captioned = Do<CurriculumWork>(new("Save", Work: withPhoto with { Photos = [photo with { Caption = "Facciata nord dopo l'intervento", Type = "png", FileName = "altro.png" }, new("0123456789abcdef0123456789abcdef", "inventata.jpg", "jpg")] }));
        Check(captioned.Photos!.Single() is { Caption: "Facciata nord dopo l'intervento", Type: "jpg", FileName: "Facciata nord.JPG" }, "Salvataggio della scheda ha alterato le fotografie");
        Check(Do<CurriculumWork>(new("Save", Work: captioned with { Photos = [] })).Photos!.Length == 1, "Salvataggio senza elenco ha tolto le fotografie");
        var latest = Do<CurriculumSnapshot>(new("Snapshot")).Works.Single();
        var backup = Path.Combine(folder, "backup");
        Check(CurriculumService.MirrorPhotos(photoFolder, backup) == 1 && CurriculumService.MirrorPhotos(photoFolder, backup) == 0 && File.Exists(Path.Combine(backup, latest.Id, photo.Id + ".jpg")), "Fotografie non copiate una sola volta nel backup");

        Reject(() => Do<byte[]>(new("Template")), "Modello non configurato restituito");
        File.WriteAllBytes(template, new byte[] { 80, 75, 3, 4 }); configured = template;
        Check(Do<CurriculumSnapshot>(new("Snapshot")).TemplateConfigured && Do<byte[]>(new("Template")).Length == 4, "Modello Word configurato non restituito");
        configured = Path.Combine(folder, "modello.exe"); Check(!Do<CurriculumSnapshot>(new("Snapshot")).TemplateConfigured, "File non Word accettato come modello");

        var removed = Do<CurriculumWork>(new("DeletePhoto", Id: latest.Id, Revision: latest.Revision, PhotoId: photo.Id));
        Check(removed.Photos!.Length == 0 && !File.Exists(Path.Combine(photoFolder, latest.Id, photo.Id + ".jpg")), "Fotografia non rimossa");
        Reject(() => Do<bool>(new("Delete", Id: removed.Id, Revision: 1)), "Eliminazione su revisione superata");
        Check(Do<bool>(new("Delete", Id: removed.Id, Revision: removed.Revision)) && Do<CurriculumSnapshot>(new("Snapshot")).Works.Length == 0, "Lavoro non eliminato");
        Reject(() => service.Execute(new("Snapshot"), " "), "Utente senza identità ammesso");
        Reject(() => Do<object>(new("Altro")), "Comando sconosciuto accettato");
        Console.WriteLine("CurriculumChecks: lavori, collegamenti per codice, fotografie, modello Word e backup verificati.");
    }
}
