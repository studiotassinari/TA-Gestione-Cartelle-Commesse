using TA.Transport;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class MainViewModel
{
    public Command GeologyCommand { get; private set; } = null!;
    private Views.GeologyWindow? geologyWindow;
    private void ShowGeology()
    {
        if (geologyWindow != null)
        {
            if (geologyWindow.WindowState == System.Windows.WindowState.Minimized) geologyWindow.WindowState = System.Windows.WindowState.Normal;
            geologyWindow.Show(); geologyWindow.Activate(); return;
        }
        var server = connection;
        // Nessun Owner: finestra indipendente, non modale e con propria voce nella barra applicazioni.
        geologyWindow = new(new GeologyViewModel(server == null ? null : token => LoadGeology(server, token), server == null ? null : async (change, token) => { await server.SendAsync<bool>(new("SaveGeologicalPosition", GeologicalPosition: change), token); }));
        geologyWindow.Closed += (_, _) => geologyWindow = null;
        geologyWindow.Show();
    }
    private static async Task<GeologyCatalog> LoadGeology(ServerConnection server, CancellationToken token)
    {
        var rows = new List<GeologicalReport>(); GeologyPage? page = null;
        do
        {
            page = await server.SendAsync<GeologyPage>(new("Geology", Geology: new(Offset: page?.NextOffset ?? 0, Limit: 500, Version: page?.Version)), token);
            rows.AddRange(page.Reports);
            if (rows.Count > 50000 || (page.NextOffset.HasValue && page.NextOffset <= page.Offset)) throw new System.IO.InvalidDataException("Paginazione del catalogo non valida.");
        } while (page.NextOffset.HasValue);
        return new(1, page.UpdatedUtc, rows, page.Warnings, page.Version);
    }
}
