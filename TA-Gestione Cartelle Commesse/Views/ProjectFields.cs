using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace TA_Gestione_Cartelle_Commesse.Views;
internal static class ProjectFields
{
    internal static void Text(Panel panel,string label,string path,bool readOnly=false)
    {
        panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,6,0,3)});
        var box=new TextBox{IsReadOnly=readOnly};
        box.SetBinding(TextBox.TextProperty,new Binding(path){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true});
        panel.Children.Add(box);
    }
    internal static void Choice(Panel panel,string label,string path,object values)
    {
        panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,6,0,3)});
        var choice=new ComboBox{ItemsSource=(System.Collections.IEnumerable)values,DisplayMemberPath="Value",SelectedValuePath="Key"};
        choice.SetBinding(ComboBox.SelectedValueProperty,new Binding(path){Mode=BindingMode.TwoWay});
        panel.Children.Add(choice);
    }
    internal static Button Button(string title,string command)
    {
        var b=new Button{Content=title,Margin=new Thickness(0,8,8,4)};
        b.SetBinding(System.Windows.Controls.Button.CommandProperty,new Binding(command));return b;
    }
}

