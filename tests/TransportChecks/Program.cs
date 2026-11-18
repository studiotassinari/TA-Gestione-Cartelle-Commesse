using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;


using TA.Transport;
using TA.Server;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Models;
using TA_Gestione_Cartelle_Commesse.Services;

using (var memory = new MemoryStream())
{
    var request = new Request("State", "C");
    await WireProtocol.WriteAsync(memory, request, default);
    memory.Position = 0;
    Check(await WireProtocol.ReadAsync<Request>(memory, default) == request, "Round-trip protocollo dedicato");
}
// Pianificazione e ore di GeCo (2.37.0): il comando viaggia nella versione 2 del protocollo; una richiesta di un client
// precedente, senza il campo GeCo, resta leggibile.
using (var memory = new MemoryStream())
{
    var request = new Request("GeCo", GeCo: new GeCoCommand("HoursSave", Salvataggio: new(11, "2026-11-16", 2, [new(1000, 100, "2026-11-17", "7:30", 2000, Note: "Sopralluogo")])));
    await WireProtocol.WriteAsync(memory, request, default);
    memory.Position = 0;
    var read = await WireProtocol.ReadAsync<Request>(memory, default);
    Check(read is { Operation: "GeCo", Version: 2, GeCo: { Action: "HoursSave", Salvataggio: { IdCdc: 11, Settimane: 2 } s } } && s.Celle.Single() is { Qta: "7:30", IdFase: 2000, Note: "Sopralluogo", IdVdc: 100 },
        "Round-trip del comando GeCo con le caselle da salvare");
    var legacy = System.Text.Json.JsonSerializer.Deserialize<Request>("""{"Operation":"Business","Category":"C","Version":2}""")!;
    Check(legacy.GeCo == null && legacy.Operation == "Business", "Richiesta di un client precedente senza il campo GeCo");
}
// Collegamento cartelle ed eventi di GeCo2027 (2.38.0): campi nuovi del comando e della sottoscrizione push; i client precedenti
// non dichiarano SupportsGeCoEvents e non ricevono notifiche che non conoscono.
using (var memory = new MemoryStream())
{
    var request = new Request("GeCo", GeCo: new GeCoCommand("ConfirmSafe", Collegamenti: [new("commessa", 1000, 7), new("gara", 3, 9)], DopoId: 42));
    await WireProtocol.WriteAsync(memory, request, default);
    memory.Position = 0;
    var read = await WireProtocol.ReadAsync<Request>(memory, default);
    Check(read.GeCo is { Action: "ConfirmSafe", DopoId: 42, Collegamenti: [{ Tipo: "commessa", Id: 1000, IdCartella: 7 }, { Tipo: "gara", IdCartella: 9 }] } && !read.SupportsGeCoEvents,
        "Round-trip del comando di collegamento con l'anteprima delle proposte sicure");
    var subscribe = System.Text.Json.JsonSerializer.Deserialize<Request>(System.Text.Json.JsonSerializer.Serialize(new Request("Subscribe", SupportsAcknowledgements: true, SupportsGeCoEvents: true)))!;
    var oldSubscribe = System.Text.Json.JsonSerializer.Deserialize<Request>("""{"Operation":"Subscribe","Version":2,"SupportsAcknowledgements":true}""")!;
    Check(subscribe.SupportsGeCoEvents && !oldSubscribe.SupportsGeCoEvents, "Sottoscrizione con gli eventi di GeCo solo se il client la dichiara");
    var oldAccess = System.Text.Json.JsonSerializer.Deserialize<GeCoAccess>("""{"Recognized":true,"Message":"","Account":"x","Planning":true}""")!;
    Check(oldAccess is { Administrator: false, Links: false }, "Risposta di accesso di un servizio precedente: nessun collegamento cartelle");
}
foreach (var header in new[] { new byte[] { 0, 0, 0, 0 }, new byte[] { 127, 255, 255, 255 }, "GET "u8.ToArray() })
{
    using var stream = new MemoryStream(header);
    await Expect<InvalidDataException>(async () => { await WireProtocol.ReadAsync<Request>(stream, default); }, "Rifiuta lunghezza non valida o richiesta HTTP");
}
using (var truncated = new MemoryStream(new byte[] { 0, 0, 0, 8, 1 }))
    await Expect<EndOfStreamException>(async () => { await WireProtocol.ReadAsync<Request>(truncated, default); }, "Rifiuta messaggio troncato");
var root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "case-" + Guid.NewGuid().ToString("N"))).FullName;
Directory.CreateDirectory(Path.Combine(root, "Documenti"));
var record = new FolderRecord("C26001", "26", "001", "Cliente", "Test", root, "Valido") { Inspection = new FolderInspection { HierarchyCaptured = true, ArchiveDirectories = ["Documenti"] } };
var state = new CategoryState('C', root, false, "", "", new ArchiveSnapshot { Records = [record] });
Check(HierarchyService.Children(state, record.Id, "").Single().Name == "Documenti", "Gerarchia della cartella nota");
foreach (var invalid in new[] { "..", "..\\esterna", "Documenti/../../", "C:\\Windows", "\\\\server\\share", "Documenti:stream" })
    await Expect<ArgumentException>(() => { HierarchyService.Children(state, record.Id, invalid); return Task.CompletedTask; }, "Rifiuta percorso esterno: " + invalid);
await Expect<KeyNotFoundException>(() => { HierarchyService.Children(state, "inesistente", ""); return Task.CompletedTask; }, "Rifiuta cartella non censita");
var html = ReportWriter.Create(root, new([record], []), root, new([], []));
Check(html.Contains("data:image/png;base64,") && html.Contains("Acherus"), "Report server senza WPF con risorse incorporate");

// Un endpoint TCP che non effettua l'autenticazione Windows non può ricevere comandi applicativi.
using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var accept = Task.Run(async () =>
{
    using var tcp = await listener.AcceptTcpClientAsync(timeout.Token);
    var buffer = new byte[1024];
    var count = await tcp.GetStream().ReadAsync(buffer, timeout.Token);
    Check(!System.Text.Encoding.UTF8.GetString(buffer, 0, count).Contains("Operation"), "Nessun comando JSON prima dell'autenticazione");
});
bool rejected = false;
try { await new ServerConnection("localhost", port).SendAsync<CategoryState>(new("State"), timeout.Token); }
catch (Exception ex) when (ex is AuthenticationException or IOException) { rejected = true; }
Check(rejected, "Rifiuta server privo di autenticazione Windows");
await accept;
Console.WriteLine("TUTTI I CONTROLLI TRASPORTO SUPERATI; autenticazione AD da collaudare sulla VM.");
static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
static async Task Expect<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Console.WriteLine("PASS: " + name); return; }
    throw new Exception("FAIL: " + name);
}
