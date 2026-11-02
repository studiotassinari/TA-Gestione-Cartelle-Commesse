using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TA_Gestione_Cartelle_Commesse.Contracts;

namespace TA.Server;

// Servizio centralizzato, senza timer, autocomplete, ripetizioni automatiche o letture documentali.
public sealed class NominatimGeocoder(string root, string endpoint, HttpClient? client = null)
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
    private sealed record RateState(DateTime NextUtc);
    public async Task<GeocodingResult> SearchAsync(string address, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 400 || address.Any(char.IsControl)) throw new ArgumentException("Indicare un indirizzo geografico valido (massimo 400 caratteri).");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("Endpoint Nominatim non valido: configurare un URL HTTPS senza credenziali o parametri.");
        address = string.Join(' ', address.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var folder = Path.Combine(root, "geocodifica"); Directory.CreateDirectory(folder);
        // Un solo lock condiviso anche fra processi: limita il traffico di tutta l'applicazione.
        FileStream? gate = null;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token); wait.CancelAfter(TimeSpan.FromSeconds(8));
        try { while (gate == null) { try { gate = new FileStream(Path.Combine(folder, "nominatim.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { await Task.Delay(150, wait.Token); } } }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Un'altra ricerca geografica è in corso. Riprova fra poco."); }
        using (gate)
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint + "\n" + address.ToUpperInvariant())));
            var cache = Path.Combine(folder, key + ".json");
            if (File.Exists(cache))
            {
                if (new FileInfo(cache).Length > 1024 * 1024) throw new IOException("Cache geografica non valida.");
                try
                {
                    var saved = JsonSerializer.Deserialize<GeocodingResult>(File.ReadAllText(cache)) ?? throw new JsonException();
                    if (saved.Candidates == null || saved.Candidates.Count > 5 || saved.Candidates.Any(c => c == null || c.Latitude is not (>= -90 and <= 90) || c.Longitude is not (>= -180 and <= 180) || string.IsNullOrWhiteSpace(c.Label))) throw new JsonException();
                    return saved with { FromCache = true };
                }
                catch (JsonException ex) { throw new IOException("Cache geografica non valida: richiedere la verifica all'amministratore.", ex); }
            }
            var rateFile = Path.Combine(folder, "nominatim-rate.json");
            DateTime next;
            try { next = File.Exists(rateFile) ? (JsonSerializer.Deserialize<RateState>(File.ReadAllText(rateFile)) ?? throw new JsonException()).NextUtc : DateTime.MinValue; }
            catch (JsonException ex) { throw new IOException("Stato del limite geografico non valido: richiedere la verifica all'amministratore.", ex); }
            var delay = next - DateTime.UtcNow;
            if (delay > TimeSpan.FromSeconds(2)) throw new IOException("Il servizio geografico ha richiesto una pausa. Riprova dopo " + next.ToLocalTime().ToString("HH:mm:ss") + ".");
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            GeologyStorage.Atomic(rateFile, new RateState(DateTime.UtcNow.AddMilliseconds(1100)));
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint + "?format=jsonv2&addressdetails=1&limit=5&accept-language=it&q=" + Uri.EscapeDataString(address));
            var appVersion = typeof(NominatimGeocoder).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            request.Headers.UserAgent.ParseAdd("TA-Commesse/" + appVersion + " (+https://github.com/studiotassinari/TA-Gestione-Cartelle-Commesse)");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using var response = await (client ?? SharedClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    var retry = response.Headers.RetryAfter?.Date?.UtcDateTime ?? DateTime.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
                    GeologyStorage.Atomic(rateFile, new RateState(retry > DateTime.UtcNow ? retry : DateTime.UtcNow.AddMinutes(1)));
                    throw new IOException("Nominatim temporaneamente occupato: richiesta non ripetuta automaticamente.");
                }
                if (!response.IsSuccessStatusCode) throw new IOException("Ricerca Nominatim non disponibile (HTTP " + (int)response.StatusCode + ").");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream(); var block = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(block, timeout.Token)) > 0) { if (buffer.Length + count > 1024 * 1024) throw new IOException("Risposta geografica troppo grande."); buffer.Write(block, 0, count); }
                var result = new GeocodingResult(Parse(buffer.ToArray()), DateTime.UtcNow, false);
                GeologyStorage.Atomic(cache, result); return result;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Il servizio geografico non ha risposto entro il tempo previsto."); }
            catch (HttpRequestException ex) { throw new IOException("Impossibile contattare Nominatim. Verificare la connessione del server.", ex); }
            catch (JsonException ex) { throw new IOException("Risposta Nominatim non valida.", ex); }
        }
    }
    public static List<GeocodingCandidate> Parse(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes); if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Elenco geografico atteso.");
        var results = new List<GeocodingCandidate>();
        foreach (var row in doc.RootElement.EnumerateArray().Take(5))
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string S(string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            if (!double.TryParse(S("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || !double.TryParse(S("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) || lat is not (>= -90 and <= 90) || lon is not (>= -180 and <= 180) || string.IsNullOrWhiteSpace(S("display_name"))) continue;
            var type = S("addresstype"); var precision = type switch { "house" or "building" => "Edificio o civico, posizione indicativa", "road" => "Strada, civico non individuato", "city" or "town" or "village" or "municipality" => "Centro abitato, non posizione dell'intervento", _ => "Elemento cartografico, posizione indicativa" };
            var source = "https://www.openstreetmap.org/?mlat=" + lat.ToString("R", CultureInfo.InvariantCulture) + "&mlon=" + lon.ToString("R", CultureInfo.InvariantCulture);
            var city = "";
            if(row.TryGetProperty("address", out var fields) && fields.ValueKind == JsonValueKind.Object)
                foreach(var field in new[]{"city","town","village","municipality"}) if(fields.TryGetProperty(field,out var value) && value.ValueKind == JsonValueKind.String) { city=value.GetString()!; break; }
            if(S("display_name").Length <= 500 && city.Length <= 500) results.Add(new(S("display_name"), lat, lon, precision, source, city));
        }
        return results;
    }
}
