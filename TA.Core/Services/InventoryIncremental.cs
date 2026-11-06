using System.Text.Json;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA_Gestione_Cartelle_Commesse.Services;
public sealed partial class InventoryStore
{
    public void RemoveConfirmed(string id)
    {
        lock(Gate)
        {
            using var db=Open();using var tx=db.BeginTransaction();
            Execute(db,tx,"DELETE FROM entries WHERE unit_id=$id",("$id",id));
            Execute(db,tx,"DELETE FROM units WHERE id=$id",("$id",id));
            Execute(db,tx,"DELETE FROM state WHERE key=$key",("$key","doc:source-signature:"+id));
            Bump(db,tx);tx.Commit();
        }
    }
    // Confronta tutti i nomi fisici e i metadati: rileva anche rinomine e modifiche a dimensione invariata.
    public bool PhysicalMatches(InventoryManifest manifest,InventoryResult probe)
    {
        if(!manifest.Complete || !probe.Complete)return false;
        var current=probe.Entries.ToDictionary(Key,StringComparer.Ordinal);
        int count=0;
        foreach(var old in Entries(manifest).Where(e=>e.Container==null))
        {
            count++;
            if(!current.TryGetValue(Key(old),out var item)||old.Bytes!=item.Bytes||old.ModifiedUtc!=item.ModifiedUtc)return false;
        }
        return count==current.Count;
    }
    public InventoryManifest Touch(InventoryManifest previous,CancellationToken token)
    {
        lock(Gate)
        {
            token.ThrowIfCancellationRequested();
            var current=Find(previous.Unit.Id)??throw new IOException("Unità non disponibile");
            if(current.Generation!=previous.Generation)throw new IOException("Unità cambiata durante il controllo");
            var updated=current with{CheckedUtc=DateTime.UtcNow,Added=0,Removed=0,Changed=0,Baseline=false};
            using var db=Open();using var tx=db.BeginTransaction();Execute(db,tx,"UPDATE units SET manifest=$json WHERE id=$id",("$json",JsonSerializer.Serialize(updated)),("$id",updated.Unit.Id));
            Bump(db,tx);tx.Commit();
            return updated;
        }
    }
}
