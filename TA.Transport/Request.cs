using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Transport;
// Comandi applicativi espliciti, senza percorsi radice modificabili dal client.
public sealed record Request(string Operation, string Category = "C", string? FolderId = null, string? RelativePath = null, int Version = 2, CreateJobRequest? Creation = null);
