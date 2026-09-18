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
        MessageBox.Show("Đang có tác vụ cài đặt. Chọn ‘Dừng hàng đợi’ rồi chờ bộ cài hiện tại kết thúc trước khi thoát.",
            "MiniApps", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void OnProgressCardClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppRow { IsWindowsSummary: true }) return;
        if (model.ToggleWindowsDetailsCommand.CanExecute(null)) model.ToggleWindowsDetailsCommand.Execute(null);
    }
}
