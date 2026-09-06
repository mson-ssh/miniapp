using System.Windows;
using MiniApps.ViewModels;

namespace MiniApps;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preview = e.Args.Contains("--preview");
        var window = new MainWindow(new MainViewModel(preview));
        MainWindow = window;
        window.Show();
    }
}
