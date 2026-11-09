using System.Windows;
using System.Windows.Data;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;
public sealed class ProjectDetailWindow : Window
{
    private ProjectDetailViewModel current;
    public event Action<ProjectDetailViewModel>? CustomerRequested;
    public ProjectDetailWindow(ProjectDetailViewModel vm)
    {
        current=vm; DataContext=vm; Width=1250;Height=850;MinWidth=950;MinHeight=600;
        SetBinding(TitleProperty,new Binding("DetailTitle"));
        var main=Application.Current.MainWindow;Resources=main.Resources;Background=main.Background;Foreground=main.Foreground;FontFamily=main.FontFamily;Icon=main.Icon;
        Content=new ProjectDetailView{Background=Background};
        current.CustomerManagementRequested+=ManageCustomer;
        Loaded+=async(_,_)=>await current.Refresh();
        Closed+=(_,_)=>{current.CustomerManagementRequested-=ManageCustomer;current.Project?.Dispose();};
    }
    private void ManageCustomer()=>CustomerRequested?.Invoke(current);
    public bool Navigate(ProjectDetailViewModel next)
    {
        if(current.Id==next.Id)return true;
        if(!current.CanLeave())return false;
        current.CustomerManagementRequested-=ManageCustomer;current.Project?.Dispose();
        current=next;DataContext=next;current.CustomerManagementRequested+=ManageCustomer;
        _=current.Refresh();return true;
    }
}
