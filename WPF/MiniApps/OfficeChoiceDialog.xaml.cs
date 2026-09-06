using System.Windows;
using System.Windows.Controls;
using MiniApps.Models;

namespace MiniApps;
public partial class OfficeChoiceDialog : Window
{
    public OfficeChoice Choice { get; private set; } = OfficeChoice.Cancel;
    public OfficeChoiceDialog() { InitializeComponent(); }
    public static OfficeChoice Ask()
    {
        var dialog = new OfficeChoiceDialog() { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Choice : OfficeChoice.Cancel;
    }
    private void Choose(object sender, RoutedEventArgs e)
    {
        Choice = (string)((Button)sender).Tag == "Office" ? OfficeChoice.Office : OfficeChoice.Wps;
        DialogResult = true;
    }
    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
