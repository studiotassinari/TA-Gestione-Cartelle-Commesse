using System.Text.Json;
namespace TA_Gestione_Cartelle_Commesse.Services;
public sealed partial class InventoryStore
{
    public string? Document(string key)
    { using var db=Open();using var cmd=Command(db,null,"SELECT payload FROM state WHERE key=$key",("$key","doc:"+key));return cmd.ExecuteScalar() as string; }
    public void SaveDocument<T>(string key,T value)
    { SaveDocumentJson(key,JsonSerializer.Serialize(value)); }
    public void SaveDocumentJson(string key,string json)
    { using var parsed=JsonDocument.Parse(json);lock(Gate){using var db=Open();Execute(db,null,"INSERT INTO state VALUES($key,$json) ON CONFLICT(key) DO UPDATE SET payload=excluded.payload WHERE state.payload<>excluded.payload",("$key","doc:"+key),("$json",json));} }
}
