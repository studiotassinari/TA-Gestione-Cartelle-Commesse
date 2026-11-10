namespace TA_Gestione_Cartelle_Commesse.Contracts;
// Contratto comune a client, trasporto e servizio: FolderId serve all’avvio; JobId alle richieste successive.
// Offset e Limit paginano i candidati già raccolti: non fanno ripartire la scansione.
public sealed record DeepInspectionQuery(string Action="avvia",string FolderId="",string JobId="",string Terms="",int Offset=0,int Limit=50);
