using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.Infrastructure;
using TA_Gestione_Cartelle_Commesse.Services;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;
public sealed partial class MainViewModel
{
    public Command CurriculumCommand { get; private set; } = null!;
    private Views.CurriculumWindow? curriculumWindow;
    // Dal menu: tutto il curriculum. Da una riga: i lavori collegati a quel codice, con un nuovo lavoro già precompilato.
    private void ShowCurriculum(WorkProjectViewModel? project = null)
    {
        if (connection == null) return;
        // Una sola finestra: se è aperta con modifiche non salvate, la chiusura viene annullata e resta quella.
        if (curriculumWindow != null) { curriculumWindow.Close(); if (curriculumWindow != null) { curriculumWindow.Activate(); return; } }
        var suggestion = project == null ? null : new CurriculumWork("", project.Subject, Customer: project.Customer?.Name ?? project.SourceCustomerName, Place: project.Location, ProjectCodes: [project.Code]);
        curriculumWindow = new(new ServerCurriculumGateway(connection), project?.Code ?? "", suggestion) { Owner = System.Windows.Application.Current.MainWindow };
        curriculumWindow.Closed += (_, _) => curriculumWindow = null;
        curriculumWindow.ProjectRequested += ShowCode;
        curriculumWindow.Show();
    }
    // Porta nell'elenco la cartella con quel codice: commesse attive o gare se presente, altrimenti archivio.
    private void ShowCode(string code)
    {
        var category = code.StartsWith('G') ? Gare : Commesse.HasCode(code) ? Commesse : Archivio;
        ActiveTabIndex = ReferenceEquals(category, Gare) ? 1 : ReferenceEquals(category, Archivio) ? 2 : 0; category.Filter = code;
        System.Windows.Application.Current.MainWindow?.Activate();
    }
}
