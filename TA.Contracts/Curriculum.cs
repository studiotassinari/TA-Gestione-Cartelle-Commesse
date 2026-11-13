namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Curriculum dello studio: lavori descritti a mano, collegabili per codice a commesse, gare e voci d'archivio.
// Il collegamento è per codice (C26001), che resta lo stesso quando la cartella passa dalle commesse attive all'archivio.
public sealed record CurriculumCategory(string Code,decimal Amount=0,string Description="");
public sealed record CurriculumPhoto(string Id,string FileName,string Type,string Caption="");
public sealed record CurriculumWork(string Id,string Title,string Customer="",string Place="",string Description="",string Services="",string Role="",
    DateOnly? Start=null,DateOnly? End=null,decimal? WorksAmount=null,decimal? FeeAmount=null,CurriculumCategory[]? Categories=null,string Reference="",
    string[]? Keywords=null,string[]? ProjectCodes=null,CurriculumPhoto[]? Photos=null,int Revision=0,string UpdatedBy="",DateTime? UpdatedUtc=null);
// Cartella oggi presente con quel codice: categoria C, G oppure A (archivio).
public sealed record CurriculumProject(string Id,string Code,string Category,string Label);
public sealed record CurriculumSnapshot(CurriculumWork[] Works,CurriculumProject[] Projects,bool TemplateConfigured);
public sealed record CurriculumCommand(string Action,CurriculumWork? Work=null,string? Id=null,int Revision=0,string? PhotoId=null,byte[]? Image=null,string? FileName=null,string? Caption=null);
