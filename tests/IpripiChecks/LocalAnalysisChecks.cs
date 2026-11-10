using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;

// Testi sintetici: nessun documento, nominativo o percorso reale.
internal static class LocalAnalysisChecks
{
    public static void Run(Action<bool, string> check)
    {
        var none = Array.Empty<string>();
        var source = new IpripiDocument("doc", "C", "folder", "C26001", "2026", "MUR A1 D1.pdf", @"\\server.example\dati\lavori\esempio.pdf", null, new string('A', 64), true);
        var version = new string('B', 64);
        const string header = "[Pagina 1]\nMODULO MUR A.1/D.1 - Asseverazione per interventi privi di rilevanza per la pubblica incolumità ai fini sismici\n";

        var declared = header + "Il progettista dichiara che l'intervento rientra nel punto B.4.1 (L1) dell'Allegato 1. Vedere anche la tavola A13.\nSostituzione di architrave su vano esistente.\n";
        var draft = IpripiLocalAnalyzer.Analyze(source, version, declared, false, none, "");
        IpripiService.Validate(draft);
        check(draft.FormType == "MUR A.1/D.1" && draft.Outcome == "IPRiPI individuati" && draft.Interventions.Count == 1 && draft.Interventions[0] is { RuleCode: "B.4.1", Level: "L1", Basis: "Dichiarato" },
            "Analisi locale: codice dichiarato nel modulo riconosciuto, sigle di tavola ignorate");
        check(IpripiLocalAnalyzer.QuotesMatch(draft, declared) && draft.Warnings.Contains(IpripiLocalAnalyzer.Notice), "Analisi locale: citazioni esatte e avviso di proposta automatica");
        var row = source with { Saved = new IpripiAnalysisRecord("r", DateTime.UtcNow, LocalAnalysis.Author, source, draft) };
        check(row.CatalogEligible && row.Status.Contains("analisi automatica locale"), "Analisi locale: voce in catalogo distinta dalle proposte salvate su richiesta");

        var unclear = header + "L'intervento rientra nel punto A.4.4 dell'elenco A.\n";
        draft = IpripiLocalAnalyzer.Analyze(source, version, unclear, false, none, "");
        IpripiService.Validate(draft);
        check(draft.Outcome == "Da verificare" && draft.Interventions.Single().Level == "" && draft.Interventions[0].MissingData.Count == 1, "Analisi locale: livello non leggibile resta da verificare");
        draft = IpripiLocalAnalyzer.Analyze(source, version, header + "L'intervento rientra nel punto A.4.4 (L2) dell'elenco A.\n", true, none, "");
        IpripiService.Validate(draft);
        check(draft.Outcome == "IPRiPI individuati" && draft.Interventions.Single().Level == "L2" && draft.Warnings.Any(w => w.StartsWith("Lettura limitata")), "Analisi locale: livello letto accanto al codice e lettura parziale segnalata");

        var list = header + "Voci: A.1.3 A.2.2 A.3.4 A.4.1 A.6.1 B.4.1 barrare quella pertinente.\n";
        draft = IpripiLocalAnalyzer.Analyze(source, version, list, false, none, "");
        IpripiService.Validate(draft);
        check(draft.Outcome == "Da verificare" && draft.Interventions.Count == 0, "Analisi locale: elenco prestampato di voci non diventa una dichiarazione");

        draft = IpripiLocalAnalyzer.Analyze(source, version, header + "Nessun riferimento numerico leggibile in questo testo sintetico di prova.\n", false, none, "");
        IpripiService.Validate(draft);
        check(draft.FormType == "MUR A.1/D.1" && draft.Outcome == "Da verificare", "Analisi locale: nessun esito negativo dedotto dall'assenza di codici");

        var other = "[Pagina 1]\nRelazione tecnica illustrativa di prova, priva dei riferimenti attesi dal nome del file sintetico.\n";
        draft = IpripiLocalAnalyzer.Analyze(source, version, other, false, none, "");
        IpripiService.Validate(draft);
        check(draft.FormType == "Altro" && draft.Outcome == "Da verificare" && IpripiLocalAnalyzer.QuotesMatch(draft, other), "Analisi locale: modulo non confermato dal testo resta da verificare");

        draft = IpripiLocalAnalyzer.Analyze(source, version, "\n[Pagina 1]\n\n[Pagina 2]\n\n", false, none, "");
        IpripiService.Validate(draft);
        check(draft.Outcome == "Non leggibile" && draft.Interventions.Count == 0, "Analisi locale: scansione senza testo registrata come non leggibile");

        var report = new GeologicalReport { Id = "geo", Title = "Relazione", FileName = "Relazione.pdf", ProjectCode = "C26001", SourceFingerprint = new string('C', 64) };
        var geology = "[Pagina 1]\nRELAZIONE GEOLOGICA E GEOTECNICA\nComune di San Lazzaro - intervento di prova sintetico.\n" +
            string.Concat(Enumerable.Repeat("Il modello geologico del sito e i parametri geotecnici sono descritti nel seguito con dati sintetici. ", 4));
        var classified = GeologyLocalAnalyzer.Analyze(report, version, geology, none, null) ?? throw new Exception("Relazione sintetica non classificata");
        GeologyService.ValidateAnalysis(classified);
        check(classified.Kind == "Geologica e geotecnica" && classified.Municipality == "San Lazzaro" && classified.Evidence.Any(e => e.Field == "localita") && GeologyLocalAnalyzer.QuotesMatch(classified, geology),
            "Analisi locale: relazione geologica e geotecnica classificata con citazioni esatte");
        var unrelated = "[Pagina 1]\n" + string.Concat(Enumerable.Repeat("Computo metrico estimativo di prova con sole voci economiche sintetiche e nessun altro contenuto. ", 4));
        classified = GeologyLocalAnalyzer.Analyze(report, version, unrelated, none, null) ?? throw new Exception("Documento sintetico non classificato");
        GeologyService.ValidateAnalysis(classified);
        check(classified.Kind == "Non pertinente" && classified.Municipality == "", "Analisi locale: candidato dal nome smentito dal contenuto");
        check(GeologyLocalAnalyzer.Analyze(report, version, "[Pagina 1]\n\n", none, null) == null, "Analisi locale: relazione senza testo non produce un'analisi");

        var limits = new LocalAnalysisLimits(true, 20, 7, 30);
        check(limits.InWindow(new DateTime(2026, 1, 1, 23, 0, 0)) && limits.InWindow(new DateTime(2026, 1, 1, 6, 59, 0)) && !limits.InWindow(new DateTime(2026, 1, 1, 7, 0, 0)) && !limits.InWindow(new DateTime(2026, 1, 1, 12, 0, 0)),
            "Limiti: fascia oraria a cavallo della mezzanotte");
        check(new LocalAnalysisLimits(true, 8, 8, 30).InWindow(new DateTime(2026, 1, 1, 12, 0, 0)), "Limiti: inizio uguale a fine significa tutto il giorno");
        var night = new DateTime(2026, 1, 10, 22, 0, 0, DateTimeKind.Utc);
        check(limits.Wait(night, null, false, false).Delay == TimeSpan.Zero && limits.Wait(night, night.AddSeconds(-30), false, false).Delay == TimeSpan.FromSeconds(90),
            "Limiti: 30 documenti l'ora impongono 120 secondi tra un documento e il successivo");
        check(limits.Wait(night, null, true, false).Delay > TimeSpan.Zero && limits.Wait(night, null, false, true).Delay > TimeSpan.Zero, "Limiti: pausa durante scansioni e letture su richiesta");
        check(limits.Wait(new DateTime(2026, 1, 10, 11, 0, 0, DateTimeKind.Utc), null, false, false).Delay > TimeSpan.Zero, "Limiti: nessuna analisi fuori dalla fascia oraria");
        check(LocalAnalysisLimits.From(new ServerSettings { EnableLocalAnalysis = true, LocalAnalysisDocumentsPerHour = 100000, LocalAnalysisStartHour = 99 }) is { DocumentsPerHour: 120, StartHour: 23 },
            "Limiti: valori eccessivi ricondotti al massimo ammesso");
        check(!LocalAnalysisLimits.From(new ServerSettings()).Enabled, "Limiti: analisi automatica disattivata se non configurata");
    }
}
