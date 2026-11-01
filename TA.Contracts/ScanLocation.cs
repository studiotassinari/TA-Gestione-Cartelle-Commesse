namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Posizione corrente: negli archivi il separatore > distingue la cartella interna dal percorso fisico.
public sealed record ScanLocation(string Category,string Phase,string Path);
