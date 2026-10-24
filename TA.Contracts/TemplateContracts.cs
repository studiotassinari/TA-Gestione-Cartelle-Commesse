namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Il client trasmette solo campi anagrafici: radice e modello sono scelti dal server.
public sealed record CreateJobRequest(string Code, string Client, string Place, string Subject, string? TemplateStamp = null);
public sealed record TemplatePreview(string Destination, string Template, string[] Directories, string Stamp);
