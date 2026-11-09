using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TA_Gestione_Cartelle_Commesse.Contracts;
using TA_Gestione_Cartelle_Commesse.ViewModels;
namespace TA_Gestione_Cartelle_Commesse.Views;

// La vista gestisce solo apertura e tastiera; selezione e ricerca restano nel modello riutilizzabile.
public sealed class CustomerAutocompleteView : UserControl
{
    private readonly TextBox search=new();
    private readonly ListBox results=new(){DisplayMemberPath="Name",MaxHeight=240};
    private readonly Popup popup=new(){StaysOpen=false,AllowsTransparency=true,Placement=PlacementMode.Bottom};
    private CustomerSelectionViewModel? model;
    private bool syncing;
    public CustomerAutocompleteView()
    {
        Content=search;popup.PlacementTarget=search;
        results.DisplayMemberPath="";
        var template=new DataTemplate();var row=new FrameworkElementFactory(typeof(StackPanel));
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));row.AppendChild(name);
        var vat=new FrameworkElementFactory(typeof(TextBlock));vat.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Vat"){StringFormat="P. IVA: {0}"});row.AppendChild(vat);
        template.VisualTree=row;results.ItemTemplate=template;
        popup.Child=new Border{Child=results,BorderThickness=new Thickness(1),BorderBrush=System.Windows.SystemColors.ActiveBorderBrush,Background=System.Windows.SystemColors.WindowBrush};
        search.TextChanged+=(_,_)=>{if(syncing||model==null)return;model.Filter=search.Text;results.ItemsSource=model.Rows;results.Width=search.ActualWidth;popup.IsOpen=search.IsKeyboardFocusWithin;};
        search.GotKeyboardFocus+=(_,_)=>search.SelectAll();
        search.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down){results.ItemsSource=model?.Rows;results.Width=search.ActualWidth;popup.IsOpen=true;results.Focus();if(results.Items.Count>0)results.SelectedIndex=0;e.Handled=true;}else if(e.Key==Key.Escape){popup.IsOpen=false;Sync();e.Handled=true;}else if(e.Key==Key.Enter&&popup.IsOpen&&results.Items.Count==1){Choose((Customer)results.Items[0]);e.Handled=true;}};
        results.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&results.SelectedItem is Customer c){Choose(c);e.Handled=true;}else if(e.Key==Key.Escape){popup.IsOpen=false;search.Focus();Sync();e.Handled=true;}};
        results.PreviewMouseLeftButtonUp+=(_,e)=>{if(ItemsControl.ContainerFromElement(results,e.OriginalSource as DependencyObject) is ListBoxItem{Content:Customer c})Choose(c);};
        popup.Closed+=(_,_)=>Sync();
        search.LostKeyboardFocus+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{if(!popup.IsOpen)Sync();}));
        DataContextChanged+=(_,_)=>Attach();Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>{popup.IsOpen=false;if(model!=null)model.PropertyChanged-=Changed;model=null;};
    }
    private void Attach(){if(model!=null)model.PropertyChanged-=Changed;model=DataContext as CustomerSelectionViewModel;if(model!=null)model.PropertyChanged+=Changed;Sync();}
    private void Changed(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(CustomerSelectionViewModel.SelectedCustomer)&&!search.IsKeyboardFocusWithin)Sync();}
    private void Sync(){syncing=true;search.Text=model?.SelectedCustomer?.Name??"";syncing=false;}
    private void Choose(Customer c){model?.Select(c);popup.IsOpen=false;Sync();search.Focus();}
}
