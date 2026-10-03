using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using MiniApps.Services;

namespace MiniApps;

public partial class InformationView : UserControl, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<CancellationToken, Task<InformationReport>> read;
    private InformationReport? report;
    private bool loading;
    private readonly bool preview;
    internal InformationView(Func<CancellationToken, Task<InformationReport>>? read = null, bool preview = false)
    {
        InitializeComponent();
        this.read = read ?? InformationService.ReadAsync;
        this.preview = preview;
        IsVisibleChanged += async (_, _) => { if (IsVisible && report == null) await ReloadAsync(); };
    }
    internal async Task ReloadAsync()
    {
        if (loading || lifetime.IsCancellationRequested) return;
        loading = true; RefreshButton.IsEnabled = false; DriverButton.IsEnabled = false;
        StatusText.Text = "Đang đọc thông tin hệ thống…";
        try
        {
            var result = await read(lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            Show(result);
            StatusText.Text = "Đã cập nhật · " + DateTime.Now.ToString("HH:mm:ss") + "  ·  Chọn văn bản để sao chép từng thông số.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        // A failed refresh keeps showing the data read earlier.
        catch (Exception ex) { StatusText.Text = "Không đọc được thông tin: " + ex.Message; }
        finally
        {
            loading = false; RefreshButton.IsEnabled = true;
            DriverButton.IsEnabled = report != null;
        }
    }
    internal void Show(InformationReport value)
    {
        report = value;
        InformationContent.DataContext = value;
        InformationContent.Visibility = Visibility.Visible;
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
    public void Dispose() => lifetime.Cancel();
}
