using TA_Gestione_Cartelle_Commesse.Infrastructure;
namespace TA_Gestione_Cartelle_Commesse.ViewModels;

public sealed partial class MainViewModel
{
    public Command IpripiCommand { get; private set; } = null!;
    private Views.IpripiWindow? ipripiWindow;
    private void ShowIpripi()
    {
        if (ipripiWindow != null)
        {
            if (ipripiWindow.WindowState == System.Windows.WindowState.Minimized) ipripiWindow.WindowState = System.Windows.WindowState.Normal;
            ipripiWindow.Show(); ipripiWindow.Activate(); return;
        }
        var server = connection;
        ipripiWindow = new(new IpripiViewModel(server == null ? null : (query, token) => server.SendAsync<Contracts.IpripiPage>(new("Ipripi", Ipripi: query), token)));
        ipripiWindow.Closed += (_, _) => ipripiWindow = null;
        ipripiWindow.Show();
    }
}
