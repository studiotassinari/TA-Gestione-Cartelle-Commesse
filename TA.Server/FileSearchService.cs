using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;
public sealed class FileSearchService(ServerSettings settings)
{
 private readonly InventoryStore store=new(settings.InventoryFolder);
 public FileSearchPage Search(FileSearchQuery query,CancellationToken token=default)=>store.Search(query,token);
}
