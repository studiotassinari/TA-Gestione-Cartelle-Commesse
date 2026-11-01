using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TA.Server.Documents;
using TA_Gestione_Cartelle_Commesse.Contracts;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;

static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
static void Denied(Action action,string message){try{action();}catch(Exception e)when(e is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException){Check(true,message);return;}throw new Exception("Non rifiutato: "+message);}
static byte[] Package(params (string Name,string Xml)[] parts)
{using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))foreach(var part in parts){using var w=new StreamWriter(zip.CreateEntry(part.Name).Open(),new UTF8Encoding(false));w.Write(part.Xml);}return memory.ToArray();}
var root=Path.Combine(Path.GetTempPath(),"TA-DocumentChecks-"+Guid.NewGuid().ToString("N"));
var source=Directory.CreateDirectory(Path.Combine(root,"lavori")).FullName;
var folder=Directory.CreateDirectory(Path.Combine(source,"2026","C26198 - Cliente - Progetto")).FullName;
var id=new string('A',64);var text="Documento di prova: costo 125,00 euro. Nessuna modifica agli originali.";
try
{
    File.WriteAllText(Path.Combine(folder,"relazione.txt"),text,new UTF8Encoding(false));
    var docx=Package(("word/document.xml","<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Relazione Word con tabella</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Valore 42</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>"));
    File.WriteAllBytes(Path.Combine(folder,"relazione.docx"),docx);
    var xlsx=Package(("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Computo' sheetId='1' r:id='r1'/></sheets></workbook>"),
        ("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Target='worksheets/sheet1.xml'/></Relationships>"),
        ("xl/sharedStrings.xml","<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Importo</t></si></sst>"),
        ("xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c r='A1' t='s'><v>0</v></c><c r='B1'><f>SUM(B2:B4)</f><v>125</v></c></row></sheetData></worksheet>"));
    File.WriteAllBytes(Path.Combine(folder,"computo.xlsx"),xlsx);
    var pdf=new PdfDocumentBuilder();var font=pdf.AddStandard14Font(Standard14Font.Helvetica);pdf.AddPage(595,842).AddText("Documento PDF di prova",12,new PdfPoint(40,700),font);File.WriteAllBytes(Path.Combine(folder,"prova.pdf"),pdf.Build());
    foreach(var extension in new[]{"zip","7z"})
    {
        var archivePath=Path.Combine(folder,"consegna."+extension);
        if(extension=="zip")
        {using var zip=ZipFile.Open(archivePath,ZipArchiveMode.Create);using(var w=new StreamWriter(zip.CreateEntry("Progetto/relazione.txt").Open()))w.Write(text);using(var s=zip.CreateEntry("Progetto/relazione.docx").Open())s.Write(docx);using(var s=zip.CreateEntry("Progetto/computo.xlsx").Open())s.Write(xlsx);}
        else
        {using var file=File.Create(archivePath);using var zip=WriterFactory.OpenWriter(file,ArchiveType.SevenZip,new SevenZipWriterOptions(CompressionType.LZMA2));using var input=new MemoryStream(Encoding.UTF8.GetBytes(text));zip.Write("Progetto/relazione.txt",input,DateTime.UtcNow);using var d=new MemoryStream(docx);zip.Write("Progetto/relazione.docx",d,DateTime.UtcNow);}
    }
    var before=Directory.GetFiles(folder).ToDictionary(p=>p,p=>(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),File.GetLastWriteTimeUtc(p)));
    DocumentPage Read(string path,string? inner=null,int offset=0,int length=12000,string? version=null)=>DocumentWorker.Execute(new(source,folder,new(id,path,inner,offset,length,version))).Document!;
    var first=Read("relazione.txt",length:10);var second=Read("relazione.txt",offset:10,version:first.Version);
    Check(first.Text+second.Text==text && first.NextOffset==10,"Testo paginato con versione del documento e nessuna perdita");
    Check(Read("relazione.docx").Text.Contains("Valore 42"),"DOCX: testo e celle delle tabelle");
    var cells=Read("computo.xlsx");Check(cells.Text.Contains("Computo")&&cells.Text.Contains("A1: Importo")&&cells.Text.Contains("B1: 125")&&cells.Text.Contains("non ricalcolato"),"XLSX: fogli, celle, stringhe e formule senza esecuzione");
    Check(Read("prova.pdf").Text.Contains("Documento PDF di prova")&&Read("prova.pdf").Text.Contains("Pagina 1"),"PDF: testo con riferimento alla pagina");
    foreach(var ext in new[]{"zip","7z"})
    {
        Check(Read("consegna."+ext,"Progetto/relazione.txt").Text==text,"Contenuto singolo file dentro "+ext);
        Check(Read("consegna."+ext,"Progetto/relazione.docx").Text.Contains("Valore 42"),"DOCX dentro "+ext);
        var listing=DocumentWorker.Execute(new(source,folder,List:new(id,"consegna."+ext,"Progetto",Extension:"txt"))).Listing!;
        Check(listing.Total==1&&listing.Entries[0].ArchiveEntry=="Progetto/relazione.txt","Elenco archivio filtrato e relativo, senza lettura documenti: "+ext);
        var archivedRoot=DocumentWorker.Execute(new(source,Path.Combine(folder,"consegna."+ext),new(id,"","Progetto/relazione.txt"))).Document!;
        Check(archivedRoot.Text==text,"Radice archivio supportata: "+ext);
    }
    Check(before.All(p=>p.Value==(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))),File.GetLastWriteTimeUtc(p.Key)))&&Directory.GetFiles(folder).Length==before.Count,"Originali invariati: impronte, date e nessun file estratto o salvato");
    var isolated=await DocumentService.RunWorkerAsync(new(source,folder,new(id,"relazione.docx")),default);
    Check(isolated.Document!.Text.Contains("Valore 42"),"Lettura reale nel processo separato, senza avviare il servizio");
    File.AppendAllText(Path.Combine(folder,"relazione.txt")," cambiato");Denied(()=>Read("relazione.txt",offset:10,version:first.Version),"Versione cambiata rifiutata tra pagine");
    foreach(var path in new[]{"../fuori.txt","C:\\Windows\\win.ini","relazione.txt:stream","sub/../relazione.txt"})Denied(()=>Read(path),"Percorso non ammesso: "+path);
    Denied(()=>Read("consegna.zip","../fuori.txt"),"Percorso archivio non valido rifiutato");
    Denied(()=>DocumentWorker.Execute(new(source,root,new(id,"relazione.txt"))),"Radice esterna alle sorgenti rifiutata");
    var outside=Path.Combine(root,"esterno.txt");File.WriteAllText(outside,"ESTERNO");var link=Path.Combine(folder,"link.txt");
    try{File.CreateSymbolicLink(link,outside);Denied(()=>Read("link.txt"),"Collegamento fuori sorgente rifiutato");}catch(Exception ex) when(ex is UnauthorizedAccessException || ex is IOException && (ex.HResult & 0xffff)==1314){Console.WriteLine("SKIP: creazione symlink non autorizzata dal runner");}
    File.WriteAllBytes(Path.Combine(folder,"xxe.docx"),Package(("word/document.xml","<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><a>&x;</a>")));
    try{Read("xxe.docx");throw new Exception("DTD accettato");}catch(InvalidDataException){Check(true,"DTD e risorse esterne XML rifiutati senza riportare contenuti nella diagnostica");}
    File.WriteAllText(Path.Combine(folder,"lungo.txt"),new string('X',200001));var truncated=Read("lungo.txt");Check(truncated.Truncated&&truncated.TotalExtractedCharacters==200000,"Testo eccessivo dichiarato parziale");
    File.WriteAllText(Path.Combine(folder,"vecchio.doc"),"non un doc");Denied(()=>Read("vecchio.doc"),"Formato legacy non spacciato per testo letto");
    using(var zip=ZipFile.Open(Path.Combine(folder,"duplicati.zip"),ZipArchiveMode.Create))
    {foreach(var value in new[]{"prima","seconda"}){using var writer=new StreamWriter(zip.CreateEntry("prova.txt").Open());writer.Write(value);}}
    Denied(()=>Read("duplicati.zip","prova.txt"),"Nomi duplicati in archivio rifiutati");
    using(var large=File.Create(Path.Combine(folder,"grande.txt")))large.SetLength(50L*1024*1024+1);
    Denied(()=>Read("grande.txt"),"Documento oltre 50 MiB rifiutato prima del parser");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{await DocumentService.RunWorkerAsync(new(source,folder,new(id,"relazione.txt")),cancelled.Token);throw new Exception("Annullamento ignorato");}catch(OperationCanceledException){Check(true,"Annullamento rispettato senza lavoro in coda");}
}
finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("TA-DocumentChecks-"))Directory.Delete(root,true);}
