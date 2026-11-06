using System.Text.Json;
using System.Text.RegularExpressions;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA.Server;

// Solo nomi/formati generati dal gestionale, nella sua cartella di output già migrata.
public static class LegacyDataCleanup
{
    public static int Clean(ServerSettings settings,InventoryStore store,CancellationToken token)
    {
        int count=0;
        void Delete(string path,string scope)
        {
            token.ThrowIfCancellationRequested();var full=Path.GetFullPath(path);var root=Path.GetFullPath(scope).TrimEnd('\\','/');
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Pulizia fuori dall'archivio previsto");
            if(File.Exists(full) && (File.GetAttributes(full)&FileAttributes.ReparsePoint)==0){File.Delete(full);count++;}
        }
        foreach(var (prefix,root) in new[]{('C',settings.JobsRoot),('G',settings.BidsRoot),('A',settings.ArchivedJobsRoot)})
        {
            var data=store.Document("snapshot:"+prefix+":"+SharedArchiveService.Normalize(root));
            if(data==null)continue;
            var current=JsonSerializer.Deserialize<ArchiveSnapshot>(data)!;
            var old=SharedArchiveService.Load(settings.ArchiveFolder,root,prefix);
            if(old==null)continue;
            // Se differiscono, la migrazione del riepilogo non è dimostrata: lascia intatto il vecchio indice.
            if(JsonSerializer.Serialize(current)!=JsonSerializer.Serialize(old))continue;
            var index=SharedArchiveService.FilePath(settings.ArchiveFolder,root,prefix);
            var years=Path.ChangeExtension(index,"years");
            if(Directory.Exists(years) && (File.GetAttributes(years)&FileAttributes.ReparsePoint)==0)
                foreach(var file in Directory.EnumerateFiles(years))
                    if(Regex.IsMatch(Path.GetFileName(file),@"^[A-Za-z0-9_-]+-[A-Fa-f0-9]{64}\.json$"))Delete(file,years);
            Delete(index,settings.ArchiveFolder);Delete(index+".lock",settings.ArchiveFolder);Delete(index+".legacy-v1.bak",settings.ArchiveFolder);
        }
        foreach(var (relative,key) in new[]{("geologia/analisi","geologia/analisi/indice.json"),("geologia/fascicoli","geologia/analisi/indice.json"),("geologia/unita","geologia/catalogo.json"),("ipripi/revisioni","ipripi/indice.json"),("ipripi/unita","ipripi/candidati.json")})
        {
            if(store.Document(key.ToUpperInvariant())==null)continue;
            var folder=Path.Combine(settings.ArchiveFolder,relative.Replace('/',Path.DirectorySeparatorChar));
            if(!Directory.Exists(folder)||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
            foreach(var file in Directory.EnumerateFiles(folder,"*.json",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}))
                if(Regex.IsMatch(Path.GetFileName(file),@"^([A-Fa-f0-9]{32}|[A-Fa-f0-9]{64}|indice)\.json$"))Delete(file,folder);
        }
        foreach(var relative in new[]{"geologia/catalogo.json","geologia/posizioni.json","geologia/analisi/indice.json","ipripi/indice.json","ipripi/candidati.json"})
            if(store.Document(relative.ToUpperInvariant())!=null)Delete(Path.Combine(settings.ArchiveFolder,relative.Replace('/',Path.DirectorySeparatorChar)),settings.ArchiveFolder);
        var inventory=Path.Combine(settings.ArchiveFolder,"inventario-file");
        if(Directory.Exists(inventory))foreach(var file in Directory.EnumerateFiles(inventory,"*.json"))
            if(Regex.IsMatch(Path.GetFileName(file),@"^report(-\d{8}T\d{6})?\.json$"))Delete(file,inventory);
        return count;
    }
}
