using GeCo.Dati;
using GeCo.Dati.Regole;
using Microsoft.EntityFrameworkCore;
using TA_Gestione_Cartelle_Commesse.Contracts;
namespace TA.Server.SharedDatabase;

// Clienti di GeCo per il catalogo di GeCa (2.40.0): l'anagrafica dei clienti è una sola, quella di GeCo, letta in sola lettura con
// ServizioAllineamento.ClientiGeCoAsync (clienti della società dello studio, nessun importo). I client chiedono il catalogo
// spesso (progetti, curriculum, creazione da modello): la lettura si tiene in memoria per un minuto e un database non
// raggiungibile lascia l'ultima lettura riuscita, così il catalogo non sparisce per un guasto passeggero.
// I clienti si riconoscono per P.IVA e codice fiscale (IdentitaClienti): Id «geco:<Cli_Id>» per non confondersi con quelli locali.
public sealed class GeCoCustomerSource
{
    public const string Prefix = "geco:";
    public sealed record Catalog(IReadOnlyList<ClienteGeCo> Clienti, DateTime ReadUtc, int IdSocieta, int VersioneDatabase);

    private readonly Func<GeCoDbContext>? open;
    private readonly Func<CancellationToken, Task<int>> version;
    private readonly int companyId;
    private readonly TimeSpan lifetime;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Catalog? last;
    // Dopo una lettura non riuscita si riprova solo dopo lifetime: con il database irraggiungibile i comandi del gestionale non
    // devono aspettare ogni volta il tempo massimo di collegamento.
    private DateTime failedUtc = DateTime.MinValue;
    public string? LastError { get; private set; }
    private bool Fresh => last is { } l && DateTime.UtcNow - l.ReadUtc < lifetime || DateTime.UtcNow - failedUtc < lifetime;

    public GeCoCustomerSource(ServerSettings settings) : this(GeCoWorkService.Open(settings.SharedDatabase),
        token => AggiornamentoDatabase.VersioneAttualeAsync(settings.SharedDatabase, token), settings.GeCoCompanyId) { }
    public GeCoCustomerSource(Func<GeCoDbContext>? open, Func<CancellationToken, Task<int>> version, int companyId = 0, TimeSpan? lifetime = null)
    { this.open = open; this.version = version; this.companyId = companyId; this.lifetime = lifetime ?? TimeSpan.FromMinutes(1); }

    public bool Configured => open != null;
    // Dopo un'importazione in GeCo il catalogo si rilegge alla richiesta successiva.
    public void Invalidate() { var l = last; if (l != null) last = l with { ReadUtc = DateTime.MinValue }; failedUtc = DateTime.MinValue; }

    public async Task<Catalog?> LoadAsync(CancellationToken token = default)
    {
        if (open == null) return null;
        if (Fresh) return last;
        await gate.WaitAsync(token);
        try
        {
            if (Fresh) return last;
            await using var db = open();
            var current = await version(token);
            // Stessa società dei comandi di GeCo (GeCoWorkService): quella configurata o la prima del database.
            var company = companyId > 0 ? companyId : await db.tblSocieta.AsNoTracking().OrderBy(s => s.Soc_Id).Select(s => s.Soc_Id).FirstOrDefaultAsync(token);
            var clients = await ServizioAllineamento.ClientiGeCoAsync(db, current, company, token);
            LastError = null;
            return last = new(clients, DateTime.UtcNow, company, current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = "Clienti di GeCo non letti: " + ex.Message;
            failedUtc = DateTime.UtcNow;
            return last;
        }
        finally { gate.Release(); }
    }

    // Per i comandi sincroni del gestionale: al massimo dieci secondi di attesa, poi l'ultima lettura (o nessuna).
    public Catalog? Current()
    {
        if (open == null) return null;
        if (Fresh) return last;
        var task = Task.Run(() => LoadAsync());
        try { return task.Wait(TimeSpan.FromSeconds(10)) ? task.Result : last; }
        catch (AggregateException) { return last; }
    }

    public static string Id(int idCliente) => Prefix + idCliente.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static int? IdCliente(string? id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(Prefix.Length), out var n) ? n : null;

    public static Customer ToCustomer(ClienteGeCo c, DateTime readUtc)
    {
        var place = string.Join(" ", new[] { c.Cap, c.Localita, string.IsNullOrWhiteSpace(c.Provincia) ? null : "(" + c.Provincia.Trim() + ")" }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var address = string.Join(", ", new[] { c.Indirizzo, place }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var vat = c.PartitaIva?.Trim() ?? ""; var tax = c.CodiceFiscale?.Trim() ?? "";
        return new Customer(Id(c.Id), c.RagioneSociale.Trim(), vat, tax, address, (string.IsNullOrWhiteSpace(c.Email) ? c.Pec : c.Email)?.Trim() ?? "", 0,
            [new CustomerSource(Customer.GeCoOrigin, c.Codice.Trim(), c.RagioneSociale.Trim(), vat, tax, readUtc)],
            string.IsNullOrWhiteSpace(c.NomeBreve) ? null : c.NomeBreve.Trim(), Customer.GeCoOrigin);
    }

    public static ClienteEsterno ToExternal(Customer c) => new(c.Id, c.Name, Blank(c.Vat), Blank(c.TaxCode), Blank(c.Address), Blank(c.Email));
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
