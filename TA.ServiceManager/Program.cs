using System.Windows;
namespace TA.ServiceManager;
public static class Program
{
    [STAThread]
    public static void Main() => new Application().Run(new ManagerWindow());
}
