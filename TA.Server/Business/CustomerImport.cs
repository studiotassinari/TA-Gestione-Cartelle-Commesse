using Microsoft.VisualBasic.FileIO;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.Business;
public sealed record CustomerCandidate(CustomerSource Source,string Address,string Email);

public static class CustomerImport
{
    public static CustomerImportResult ImportCsv(BusinessService service,string csv,string actor,string provider="Magoot",bool preview=false)
    {
        if(string.IsNullOrWhiteSpace(csv))throw new ArgumentException("Esportazione CSV vuota.");
        if(csv.Length>2_000_000)throw new ArgumentException("CSV oltre 2 MB: suddividere l'esportazione.");
        using var parser=new TextFieldParser(new StringReader(csv)){HasFieldsEnclosedInQuotes=true};
        parser.SetDelimiters(csv.Split('\n')[0].Contains(';')?";":",");
        var headers=(parser.ReadFields()??[]).Select(Normalize).ToArray();
        if(headers.Distinct().Count()!=headers.Length)throw new ArgumentException("Intestazioni CSV duplicate: correggere il file prima dell'importazione.");
        if(!headers.Any(h=>h is "ragionesociale" or "denominazione" or "nome" or "name" or "cliente"))throw new ArgumentException("Manca la colonna Ragione sociale/Nome.");
        var candidates=new List<CustomerCandidate>();
        while(!parser.EndOfData)
        {
            var row=parser.ReadFields()??[];if(row.Length!=headers.Length)throw new ArgumentException("Colonne CSV non coerenti.");
            string Value(params string[] names){var index=Array.FindIndex(headers,n=>names.Contains(n));return index<0?"":row[index].Trim();}
            var vat=Value("partitaiva","piva","vatnumber","vat");var tax=Value("codicefiscale","cf","taxcode");
            var id=Value("id","codice","codicecliente");if(id.Length==0)id=vat.Length>0?BusinessService.Tax(vat):BusinessService.Tax(tax);
            if(id.Length==0)throw new ArgumentException("Ogni cliente richiede Id/Codice, partita IVA o codice fiscale.");
            candidates.Add(new(new(provider,id,Value("ragionesociale","denominazione","nome","name","cliente"),vat,tax,DateTime.UtcNow),Value("indirizzo","address"),Value("email","posta")));
            if(candidates.Count>5000)throw new ArgumentException("CSV oltre 5000 clienti.");
        }
        if(candidates.GroupBy(c=>c.Source.ExternalId,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new ArgumentException("Codici cliente duplicati nella stessa esportazione: verificare prima dell'importazione.");
        return Apply(service,candidates,actor,preview);
    }
    private static string Normalize(string text)=>new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static CustomerImportResult Apply(BusinessService service,IReadOnlyList<CustomerCandidate> rows,string actor,bool preview=false)
    {
        return service.Store.Locked(()=>
        {
            var clients=service.Store.All<Customer>("customer").ToList();int added=0,linked=0;var issues=new List<string>();
            foreach(var row in rows)
            {
                var s=row.Source with{Vat=BusinessService.Tax(row.Source.Vat),TaxCode=BusinessService.Tax(row.Source.TaxCode)};
                if(string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>200||s.ExternalId.Length>200||s.Vat.Length>1000||s.TaxCode.Length>1000||row.Address.Length>1000||row.Email.Length>1000){issues.Add("Anagrafica non valida: "+s.ExternalId);continue;}
                var matches=clients.Where(c=>(c.Sources??[]).Any(x=>x.Provider==s.Provider&&x.ExternalId==s.ExternalId)||s.Vat.Length>0&&c.Vat==s.Vat||s.TaxCode.Length>0&&c.TaxCode==s.TaxCode).ToArray();
                if(matches.Length>1){issues.Add(s.Name+": più clienti corrispondenti; riconciliazione manuale necessaria.");continue;}
                var old=matches.SingleOrDefault();
                if(old!=null&&(old.Vat.Length>0&&s.Vat.Length>0&&old.Vat!=s.Vat||old.TaxCode.Length>0&&s.TaxCode.Length>0&&old.TaxCode!=s.TaxCode))
                {issues.Add(s.Name+": identificativi fiscali discordanti; dati conservati.");continue;}
                var source=(old?.Sources??[]).Where(x=>x.Provider!=s.Provider||x.ExternalId!=s.ExternalId).Append(s).ToArray();
                var item=old==null?new Customer(Guid.NewGuid().ToString("N"),s.Name,s.Vat,s.TaxCode,row.Address,row.Email,1,source):old with{
                    Vat=old.Vat.Length==0?s.Vat:old.Vat,TaxCode=old.TaxCode.Length==0?s.TaxCode:old.TaxCode,
                    Address=old.Address.Length==0?row.Address:old.Address,Email=old.Email.Length==0?row.Email:old.Email,Sources=source,Revision=old.Revision+1};
                if(old!=null&&!old.Name.Equals(s.Name,StringComparison.OrdinalIgnoreCase))issues.Add(s.Name+": mantenuta la denominazione locale «"+old.Name+"»; fonte collegata.");
                if(!preview)service.Store.Save("customer",item.Id,old?.Revision??0,item,actor,"Import "+s.Provider);
                if(old==null)added++;else{linked++;clients.Remove(old);}clients.Add(item);
            }
            return new CustomerImportResult(rows.Count,added,linked,issues.ToArray());
        });
    }
}
