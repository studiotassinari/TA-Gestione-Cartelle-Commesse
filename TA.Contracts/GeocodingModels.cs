namespace TA_Gestione_Cartelle_Commesse.Contracts;

// Il geocodificatore riceve soltanto l'indirizzo scelto dall'utente, mai documenti o nomi dei clienti.
public sealed record GeologicalGeocodeQuery(string ReportId, string Address, string Version);
public sealed record GeocodingCandidate(string Label, double Latitude, double Longitude, string Precision, string Source, string Municipality = "");
public sealed record GeocodingResult(List<GeocodingCandidate> Candidates, DateTime RetrievedUtc, bool FromCache);
