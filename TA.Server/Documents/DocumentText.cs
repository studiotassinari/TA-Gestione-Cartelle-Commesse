using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TA.Server.Documents;

// Parser locale isolato dal servizio. Non avvia Office, macro, collegamenti o servizi IA.
public sealed class DocumentText
{
    public const int MaximumCharacters=200000;
    private readonly StringBuilder text=new();
    public List<string> Warnings {get;}=[];
    public bool Truncated {get;private set;}
    public string Text=>text.ToString();
    private bool Full=>text.Length>=MaximumCharacters;
    private void Add(string value)
    {int room=MaximumCharacters-text.Length;if(value.Length>room)Truncated=true;text.Append(value.AsSpan(0,Math.Min(value.Length,room)));}
    public static DocumentText Read(byte[] bytes,string name)
    {
        var result=new DocumentText();var extension=Path.GetExtension(name).ToLowerInvariant();
        using var stream=new MemoryStream(bytes,false);
        switch(extension)
        {
            case ".pdf": result.Pdf(bytes);break;
            case ".docx": result.Word(stream);break;
            case ".xlsx": result.Excel(stream);break;
            case ".txt": case ".csv": case ".tsv": case ".md": case ".log": case ".json": case ".xml":
                result.Plain(stream);break;
            default:throw new InvalidDataException("Formato non supportato: PDF testuale, DOCX, XLSX, TXT, CSV, TSV, MD, LOG, JSON e XML. DOC/XLS e documenti cifrati richiedono conversione manuale.");
        }
        if(result.Truncated)result.Warnings.Add("Estrazione limitata a 200000 caratteri o al limite di pagine: non rappresenta l'intero documento.");
        if(result.text.Length==0)result.Warnings.Add("Nessun testo estraibile. Un PDF acquisito come immagine richiede OCR, non incluso.");
        return result;
    }
    private void Plain(Stream stream)
    {
        using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true,4096,true);
        var buffer=new char[4096];int n;
        try{while((n=reader.Read(buffer,0,buffer.Length))>0){Add(new string(buffer,0,n));if(Full){Truncated|=reader.Peek()>=0;break;}}}
        catch(DecoderFallbackException){throw new InvalidDataException("Codifica non riconosciuta: usare UTF-8 o UTF-16 con indicatore BOM.");}
    }
    private void Pdf(byte[] bytes)
    {
        using var pdf=PdfDocument.Open(bytes);
        int pages=Math.Min(pdf.NumberOfPages,1000);if(pages<pdf.NumberOfPages)Truncated=true;
        for(int p=1;p<=pages;p++)
        {if(Full){Truncated=true;break;}var value=ContentOrderTextExtractor.GetText(pdf.GetPage(p));Add($"\n[Pagina {p}]\n"+value+"\n");if(string.IsNullOrWhiteSpace(value))Warnings.Add($"Pagina {p}: nessun testo estraibile; eventuali immagini non lette.");}
        // I valori AcroForm non fanno parte del testo della pagina: riportarli separatamente.
        if (!Full && pdf.TryGetForm(out var form))
        {
            Add("\n[Campi interattivi PDF - valori dichiarati, non verifica delle firme]\n");
            int count = 0;
            foreach (var field in form.GetFields())
            {
                if (Full || ++count > 10000) { Truncated = true; break; }
                var value = field.GetFieldValue().Value;
                if (string.IsNullOrWhiteSpace(value)) continue;
                var info = field.Information;
                Add($"[Campo {count}; pagina {field.PageNumber?.ToString() ?? "non indicata"}; nome {info.PartialName}; etichetta {info.AlternateName}]\n{value}\n");
            }
            Warnings.Add("Campi interattivi estratti separatamente: verificare nomi, caselle e corrispondenza con l'aspetto visivo; nessuna validazione delle firme.");
        }
        Warnings.Add("Testo PDF: ordine di lettura, tabelle e disegni possono richiedere verifica visiva. Nessun OCR.");
    }
    private static XDocument Xml(ZipArchive zip,string name)
    {
        var entries=zip.Entries.Where(e=>e.FullName==name).Take(2).ToArray();
        if(entries.Length!=1)throw new InvalidDataException("Parte Office assente o duplicata: "+name);
        var entry=entries[0];if(entry.Length>16*1024*1024)throw new InvalidDataException("Parte Office troppo grande.");
        using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024,MaxCharactersFromEntities=1024});
        return XDocument.Load(reader);
    }
    private static ZipArchive Office(Stream stream)
    {
        var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
        if(zip.Entries.Count>10000 || zip.Entries.Sum(e=>e.Length)>128L*1024*1024){zip.Dispose();throw new InvalidDataException("Pacchetto Office oltre i limiti di lettura.");}return zip;
    }
    private void Word(Stream stream)
    {
        using var zip=Office(stream);XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var names=zip.Entries.Select(e=>e.FullName).Where(n=>n=="word/document.xml" || n=="word/footnotes.xml" || n=="word/endnotes.xml" || (n.StartsWith("word/header")||n.StartsWith("word/footer")) && n.EndsWith(".xml")).OrderBy(n=>n!="word/document.xml").ThenBy(n=>n).ToArray();
        if(!names.Contains("word/document.xml"))throw new InvalidDataException("Documento Word non valido.");
        foreach(var name in names)
        {
            if(Full){Truncated=true;break;}Add("\n["+name+"]\n");
            foreach(var node in Xml(zip,name).Descendants())
            {if(node.Name==w+"t")Add(node.Value);else if(node.Name==w+"tab")Add("\t");else if(node.Name==w+"br"||node.Name==w+"cr")Add("\n");else if(node.Name==w+"p")Add("\n");if(Full){Truncated=true;break;}}
        }
        Warnings.Add("Testo e tabelle DOCX; immagini, oggetti incorporati e contenuti esterni non letti. Impaginazione non riprodotta.");
    }
    private void Excel(Stream stream)
    {
        using var zip=Office(stream);XNamespace x="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var shared=zip.GetEntry("xl/sharedStrings.xml")==null?[]:Xml(zip,"xl/sharedStrings.xml").Descendants(x+"si").Select(e=>string.Concat(e.Descendants(x+"t").Select(t=>t.Value))).ToArray();
        var relationships=Xml(zip,"xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(e=>(string)e.Attribute("Id")!,e=>e);
        foreach(var sheet in Xml(zip,"xl/workbook.xml").Descendants(x+"sheet"))
        {
            if(Full){Truncated=true;break;}Add("\n[Foglio: "+(string?)sheet.Attribute("name")+"]\n");
            if(!relationships.TryGetValue((string?)sheet.Attribute(rel+"id")??"",out var link))throw new InvalidDataException("Relazione foglio assente.");
            if((string?)link.Attribute("TargetMode")=="External")throw new InvalidDataException("Foglio esterno escluso.");
            var target=(string?)link.Attribute("Target")??"";if(target.StartsWith('/'))target=target.TrimStart('/');else target="xl/"+target;
            if(target.Contains(':')||target.Split('/').Any(p=>p is "." or "..")||!target.StartsWith("xl/worksheets/"))throw new InvalidDataException("Percorso foglio non valido.");
            foreach(var cell in Xml(zip,target).Descendants(x+"c"))
            {
                var value=cell.Element(x+"v")?.Value??string.Concat(cell.Descendants(x+"t").Select(t=>t.Value));
                if((string?)cell.Attribute("t")=="s"){if(!int.TryParse(value,out var index)||index<0||index>=shared.Length)throw new InvalidDataException("Indice testo Excel non valido.");value=shared[index];}
                var formula=cell.Element(x+"f")?.Value;
                Add((string?)cell.Attribute("r")+": "+value+(formula==null?"":" [formula: "+formula+"; risultato salvato, non ricalcolato]")+"\n");
                if(Full){Truncated=true;break;}
            }
        }
        Warnings.Add("Valori e formule XLSX salvati, senza calcolo. Date e numeri possono essere espressi nel formato interno Excel. Nessuna macro o connessione esterna eseguita.");
    }
}
