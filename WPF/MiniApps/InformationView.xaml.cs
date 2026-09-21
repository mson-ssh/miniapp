using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Threading;
using MiniApps.Services;

namespace MiniApps;

public partial class InformationView : UserControl, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<CancellationToken, Task<InformationReport>> read;
    private InformationReport? report;
    private bool loading;
    private readonly bool preview;
    // The read reports no progress of its own, so the percentage is paced by time: about 90%
    // after 3.5 s (a typical read here), then slowing, and holding at 99% until the data arrives.
    private readonly DispatcherTimer loadingTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double loadingProgress;
    internal int LoadingPercentShown { get; private set; }
    // Raised once fresh data is on screen, so the window can size itself to it.
    internal event Action? ContentShown;
    internal bool HasData => report != null && !loading;
    // How much taller (positive) or shorter (negative) the data is than the space it has now.
    internal double ContentOverflow
    {
        get { InformationScroll.UpdateLayout(); return InformationScroll.ExtentHeight - InformationScroll.ViewportHeight; }
    }
    internal InformationView(Func<CancellationToken, Task<InformationReport>>? read = null, bool preview = false)
    {
        InitializeComponent();
        this.read = read ?? InformationService.ReadAsync;
        this.preview = preview;
        IsVisibleChanged += async (_, _) => { if (IsVisible && report == null) await ReloadAsync(); };
        loadingTimer.Tick += (_, _) => SetLoadingProgress(loadingProgress + (99 - loadingProgress) * 0.034);
    }
    private void SetLoadingProgress(double value)
    {
        loadingProgress = Math.Min(99, value);
        LoadingPercentShown = Math.Min(99, Math.Max(1, (int)Math.Ceiling(loadingProgress)));
        LoadingPercent.Text = LoadingPercentShown + "%";
        LoadingProgress.Value = LoadingPercentShown;
    }
    private void ShowLoading(bool show)
    {
        if (show)
        {
            SetLoadingProgress(1);
            InformationContent.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            loadingTimer.Start();
            return;
        }
        loadingTimer.Stop();
        LoadingPanel.Visibility = Visibility.Collapsed;
        // A failed refresh keeps showing the data read earlier.
        InformationContent.Visibility = report != null ? Visibility.Visible : Visibility.Collapsed;
        if (report != null) ContentShown?.Invoke();
    }
    internal async Task ReloadAsync()
    {
        if (loading || lifetime.IsCancellationRequested) return;
        loading = true; RefreshButton.IsEnabled = false; DriverButton.IsEnabled = false;
        ShowLoading(true);
        StatusText.Text = "";
        try
        {
            var result = await read(lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            Show(result);
            StatusText.Text = "Đã cập nhật · " + DateTime.Now.ToString("HH:mm:ss") + "  ·  Chọn văn bản để sao chép từng thông số.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { StatusText.Text = "Không đọc được thông tin: " + ex.Message; }
        finally
        {
            loading = false; RefreshButton.IsEnabled = true; ShowLoading(false);
            DriverButton.IsEnabled = report != null;
        }
    }
    internal void Show(InformationReport value)
    {
        report = value;
        InformationContent.DataContext = value;
        CopyButton.IsEnabled = true;
    }
    private async void RefreshInformation(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void OpenDriver(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenDriverSupport(report?.Serial ?? "", report?.DriverUrl ?? "",
                value => { if (!preview) Clipboard.SetText(value); },
                url => { if (!preview) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); });
            StatusText.Text = preview ? "Đã mô phỏng sao chép Serial và mở hỗ trợ driver." : "Đã sao chép Serial và mở trang hỗ trợ driver chính thức.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    internal static void OpenDriverSupport(string serial, string url, Action<string> copy, Action<string> open)
    {
        var cleanSerial = DeviceInfoService.Resolve("", "", "", serial).Serial;
        if (cleanSerial is "Không xác định" or "—") throw new InvalidOperationException("Chưa đọc được Serial. Hãy bấm Làm mới để thử lại.");
        try { copy(cleanSerial); }
        catch (Exception ex) { throw new InvalidOperationException("Không sao chép được Serial: " + ex.Message, ex); }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Đã sao chép Serial. Chưa xác định được trang hỗ trợ chính thức của hãng này.");
        try { open(url); }
        catch (Exception ex) { throw new InvalidOperationException("Đã sao chép Serial nhưng không mở được trang hỗ trợ: " + ex.Message, ex); }
    }
    private void CopyInformation(object sender, RoutedEventArgs e)
    {
        if (report == null) return;
        try { Clipboard.SetText(report.ToText()); StatusText.Text = "Đã sao chép thông tin."; }
        catch (Exception ex) { StatusText.Text = "Không sao chép được: " + ex.Message; }
    }
    public void Dispose() { loadingTimer.Stop(); lifetime.Cancel(); }
}
