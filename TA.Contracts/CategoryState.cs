using TA_Gestione_Cartelle_Commesse.Models;
namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Fotografia immutabile dello stato server: il client mantiene i dati precedenti durante una scansione.
public sealed record CategoryState(char Prefix, string Root, bool Busy, string Message,
    string ArchiveStatus, ArchiveSnapshot? Snapshot);
