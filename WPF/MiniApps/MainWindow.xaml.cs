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
        defaultMinHeight = MinHeight;
        var information = this.information = new InformationView(model.IsPreview
            ? _ => Task.FromResult(Services.InformationService.Parse("{\"OS\":\"Windows · mô phỏng\",\"CPU\":\"CPU · mô phỏng\",\"RAM\":\"16 GB · mô phỏng\",\"Manufacturer\":\"Dell\",\"Serial\":\"DEMO-123456\"}"))
            : null, preview: model.IsPreview) { Visibility = model.Page == 1 ? Visibility.Visible : Visibility.Collapsed };
        PageHost.Children.Add(information);
        PropertyChangedEventHandler onPageChanged = (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.Page)) return;
            information.Visibility = model.Page == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (model.Page == 1) FitInformationLater();
            else RestoreHeightOutsideInformation();
        };
        information.ContentShown += FitInformationLater;
        model.PropertyChanged += onPageChanged;
        Closed += (_, _) => { model.PropertyChanged -= onPageChanged; information.Dispose(); };
    }
    // Information fits its window to its data: shorter or taller as the machine reports more or
    // less, never past the screen. Other pages get their previous height back.
    private const double InformationMinHeight = 480;
    private InformationView? information;
    private double defaultMinHeight;
    private double? heightOutsideInformation;
    private void FitInformationLater() =>
        Dispatcher.BeginInvoke(new Action(FitInformationHeight), System.Windows.Threading.DispatcherPriority.ContextIdle);
    internal void FitInformationHeight()
    {
        if (information == null || model.Page != 1 || WindowState != WindowState.Normal || !information.HasData) return;
        UpdateLayout();
        var overflow = information.ContentOverflow;
        if (Math.Abs(overflow) < 1) return;
        heightOutsideInformation ??= Height;
        MinHeight = InformationMinHeight;
        var work = SystemParameters.WorkArea;
        var target = Math.Max(MinHeight, Math.Min(work.Height, ActualHeight + overflow));
        Height = target;
        // Keep the grown window on screen; a window placed off-screen on purpose is left alone.
        if (Top >= work.Top && Top + target > work.Bottom) Top = Math.Max(work.Top, work.Bottom - target);
    }
    private void RestoreHeightOutsideInformation()
    {
        if (heightOutsideInformation is not { } height) return;
        MinHeight = defaultMinHeight;
        Height = height;
        heightOutsideInformation = null;
    }
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
