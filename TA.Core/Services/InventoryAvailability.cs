using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;
public static class InventoryAvailability
{
 public static InventoryRun? Load(InventoryStore store,string category="",string year="",string code="",CancellationToken token=default)
 { token.ThrowIfCancellationRequested(); return store.Latest(); }
}
