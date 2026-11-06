using System.IO.Compression;
using System.Text;
using TA.Server.Business;

internal static class FattureExcelChecks
{
    private static byte[] Workbook(string body, string extra = "", string target = "worksheets/sheet7.xml")
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
        {
            void Part(string name, string xml) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(xml); }
            Part("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Export" sheetId="1" r:id="r1"/></sheets></workbook>""");
            Part("xl/_rels/workbook.xml.rels", $"<Relationships><Relationship Id=\"r1\" Target=\"{target}\"/></Relationships>");
            Part("xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Cliente di prova</t></si></sst>""");
            Part("xl/worksheets/sheet7.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""" +
                "<row r=\"1\">" + Cell("A1", "Denominazione") + Cell("B1", "Codice interno") + Cell("C1", "P.IVA/TAX ID") + Cell("D1", "Codice Fiscale") + Cell("E1", "CAP") + Cell("F1", "Comune") + Cell("G1", "Indirizzo e-mail") + "</row>" + body + "</sheetData>" + extra + "</worksheet>");
        }
        return data.ToArray();
    }
    private static string Cell(string reference, string value) => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>";
    public static void Run()
    {
        void Check(bool good) { if (!good) throw new Exception("Excel Fatture in Cloud: verifica fallita"); }
        void Reject(byte[] bytes) { try { FattureExcelImport.Parse(bytes); } catch (Exception e) when (e is IOException or ArgumentException or InvalidDataException) { return; } throw new Exception("Excel non valido accettato"); }
        var row = "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>0</v></c>" + Cell("B2", "0007") + Cell("C2", "00123456789") + Cell("E2", "00100") + Cell("F2", "Roma") + Cell("G2", "prova@example.invalid") + "</row>";
        var missing = "<row r=\"3\">" + Cell("A3", "Cliente senza identificativi") + "</row>";
        var result = FattureExcelImport.Parse(Workbook(row + missing));
        Check(result.Read == 2 && result.Customers.Length == 1 && result.Issues.Length == 1);
        Check(result.Customers[0].Source.ExternalId == "ID:0007" && result.Customers[0].Source.Vat == "00123456789");
        Check(result.Customers[0].Address == "00100 Roma" && result.Customers[0].Email == "prova@example.invalid");
        Reject(Workbook(row.Replace("<v>0</v>", "<f>WEBSERVICE(\"https://example.invalid\")</f><v>0</v>")));
        Reject(Workbook(row, target: "../../outside.xml"));
        var numeric = FattureExcelImport.Parse(Workbook(row.Replace(Cell("E2", "00100"), "<c r=\"E2\"><v>100</v></c>")));
        Check(numeric.Customers.Length == 0 && numeric.Issues.Length == 1);
        var duplicates = FattureExcelImport.Parse(Workbook(row + row.Replace("r=\"2\"", "r=\"3\"")));
        Check(duplicates.Customers.Length == 0 && duplicates.Read == 2);
        Reject([1, 2, 3]);
        Console.WriteLine("ExcelChecks: struttura, zeri iniziali, campi sparsi, righe senza identificativi, formule e duplicati verificati.");
    }
}
