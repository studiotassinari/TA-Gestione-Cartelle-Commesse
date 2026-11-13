namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Curriculum dello studio: lavori descritti a mano, collegabili per codice a commesse, gare e voci d'archivio.
// Il collegamento è per codice (C26001), che resta lo stesso quando la cartella passa dalle commesse attive all'archivio.
// Grado di complessità come lo riporta il committente (esempio 0,95): testo libero, non entra nei calcoli.
public sealed record CurriculumCategory(string Code,decimal Amount=0,string Description="",string Grade="");
// Macro categorie di prestazioni spuntabili sulla scheda: nell'esportazione accendono il quadrante corrispondente del simbolo.
public static class CurriculumSectors { public static readonly string[] All=["Strutture","Architettura","Impianti","Consulenza"]; }
public sealed record CurriculumPhoto(string Id,string FileName,string Type,string Caption="");
public sealed record CurriculumWork(string Id,string Title,string Customer="",string Place="",string Description="",string Services="",string Role="",
    DateOnly? Start=null,DateOnly? End=null,decimal? WorksAmount=null,decimal? FeeAmount=null,CurriculumCategory[]? Categories=null,string Reference="",
    string[]? Keywords=null,string[]? ProjectCodes=null,CurriculumPhoto[]? Photos=null,int Revision=0,string UpdatedBy="",DateTime? UpdatedUtc=null,string[]? Sectors=null);
// Cartella oggi presente con quel codice: categoria C, G oppure A (archivio).
public sealed record CurriculumProject(string Id,string Code,string Category,string Label);
public sealed record CurriculumSnapshot(CurriculumWork[] Works,CurriculumProject[] Projects,bool TemplateConfigured,bool GraphicsConfigured=false);
// Materiale grafico dello studio (logo, copertina, icone, stile): resta in una cartella del server e non entra nel programma.
public sealed record CurriculumGraphicFile(string Name,byte[] Data);
public sealed record CurriculumGraphics(CurriculumGraphicFile[] Files);
public sealed record CurriculumCommand(string Action,CurriculumWork? Work=null,string? Id=null,int Revision=0,string? PhotoId=null,byte[]? Image=null,string? FileName=null,string? Caption=null);
