namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Il percorso assoluto serve per Esplora file; quello relativo è validato dal server per la gerarchia.
public sealed record FolderEntry(string Name, string Path, string RelativePath);
