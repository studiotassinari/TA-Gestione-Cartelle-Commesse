using System.Windows;
namespace TA.ServiceManager;
public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var instance = new TA.Desktop.SessionInstance("ServerManager");
        if (!instance.IsPrimary) return;
        var application = new Application();
        var window = new ManagerWindow();
        instance.Listen(window);
        application.Run(window);
    }
}
