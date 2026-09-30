using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MiniApps.Models;
using MiniApps.ViewModels;

namespace MiniApps;
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        // Keep the consequential Office-removal notice, omit the generic instructions.
        InstallHint.Text = "WPS / OnlyOffice / LibreOffice sẽ gỡ Microsoft Office.";
        InstallHint.Margin = new Thickness(0, 8, 0, 0);
        var information = this.information = new InformationView(model.IsPreview
            ? _ => Task.FromResult(Services.InformationService.Parse(Services.InformationService.PreviewJson))
            : null, preview: model.IsPreview) { Visibility = model.Page == 1 ? Visibility.Visible : Visibility.Collapsed };
        PageHost.Children.Add(information);
        PropertyChangedEventHandler onPageChanged = (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.Page)) return;
            information.Visibility = model.Page == 1 ? Visibility.Visible : Visibility.Collapsed;
        };
        model.PropertyChanged += onPageChanged;
        Closed += (_, _) => { model.PropertyChanged -= onPageChanged; information.Dispose(); };
    }
    // Every page, Information included, keeps the window size the user has.
    private InformationView? information;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!model.IsBusy) return;
        if (model.IsCancellingInstall)
        {
            if (!model.ConfirmExitWhileCancelling()) e.Cancel = true;
            return;
        }
        e.Cancel = true;
        MessageBox.Show(
            model.IsInstallRunning ? "Đang có tác vụ cài đặt. Bấm HỦY và chờ tiến trình dừng trước khi thoát. Nếu tiến trình bị treo khi đang hủy, đóng cửa sổ lần nữa để chọn thoát ngay." : "Đang chạy tác vụ EXTEND. Hãy chờ tác vụ kết thúc trước khi thoát.",
            "MiniApps", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void OnProgressCardClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppRow { IsWindowsSummary: true }) return;
        if (model.ToggleWindowsDetailsCommand.CanExecute(null)) model.ToggleWindowsDetailsCommand.Execute(null);
    }
}
