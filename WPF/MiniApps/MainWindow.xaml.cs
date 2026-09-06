using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MiniApps.Models;
using MiniApps.ViewModels;

namespace MiniApps;
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    public MainWindow(MainViewModel model) { InitializeComponent(); this.model = model; DataContext = model; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!model.IsBusy) return;
        e.Cancel = true;
        var message = model.IsOptimizeRunning
            ? "Optimize Windows đang chạy. Hãy chờ tác vụ hoàn tất trước khi thoát."
            : "Đang có tác vụ cài đặt. Chọn ‘Dừng hàng đợi’ rồi chờ bộ cài hiện tại kết thúc trước khi thoát.";
        MessageBox.Show(message, "MiniApps", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void OnProgressCardClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppRow { IsWindowsSummary: true }) return;
        if (model.ToggleWindowsDetailsCommand.CanExecute(null)) model.ToggleWindowsDetailsCommand.Execute(null);
    }
    private void OnAddAppClick(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => { AppNameEditor.Focus(); AppNameEditor.SelectAll(); }));
    private void OnAddWindowsClick(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => { WindowsNameEditor.Focus(); WindowsNameEditor.SelectAll(); }));
}
