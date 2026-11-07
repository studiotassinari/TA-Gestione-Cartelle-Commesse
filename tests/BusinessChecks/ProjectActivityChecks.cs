using TA.Server.Business;
using TA_Gestione_Cartelle_Commesse.Contracts;
internal static class ProjectActivityChecks
{
    public static void Run(string folder)
    {
        var store=new BusinessStore(folder);
        var old=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
        var source=new WorkProject("project","C20001","Prova",LastActivityUtc:old);
        var service=new BusinessService(store,()=>[source],s=>s,()=>["reader"]);
        store.Save("phase","migrated",0,new WorkPhase("migrated",source.Id,"Fase migrata"),"server","PhaseMigration");
        store.Save("phase","generated",0,new WorkPhase("generated",source.Id,"Fase generata"),"Migrazione fasi","Phase");
        var before=store.ChangeNumber;
        var catalog=(ProjectCatalog)service.Execute(new("ProjectCatalog"),"reader",false);
        if(catalog.CanManage||catalog.Projects.Single().LastActivityUtc!=old||store.ChangeNumber!=before)
            throw new Exception("Consultazione o migrazione ha falsato ultima attività.");
        store.Save("phase","actual",0,new WorkPhase("actual",source.Id,"Fase modificata"),"manager","Phase");
        var changed=service.Projects().Single().LastActivityUtc;
        if(changed<=old)throw new Exception("Modifica fase non aggiorna commessa.");
        store.Delete("phase","actual",1,"manager");
        if(service.Projects().Single().LastActivityUtc<changed)throw new Exception("Eliminazione perde ultima attività.");
        source=source with{LastActivityUtc=DateTime.UtcNow.AddDays(1)};
        if(service.Projects().Single().LastActivityUtc!=source.LastActivityUtc)throw new Exception("Modifica file più recente ignorata.");
        Console.WriteLine("ProjectActivityChecks: catalogo in sola lettura e date da file/attività, senza timestamp di scansione.");
    }
}
