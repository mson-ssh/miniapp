using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using MiniApps.Models;
using MiniApps.Services;

namespace MiniApps.ViewModels;
public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) action(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record SuiteChoice(string Value, string Label);

public sealed class WindowsProgressTracker(int total)
{
    private readonly HashSet<string> finished = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> cancelled = new(StringComparer.OrdinalIgnoreCase);
    public double Progress => total == 0 ? 0 : finished.Count * 100d / total;
    public string Update(string id, string eventStatus, bool isFinished, bool isFailed)
    {
        if (isFinished)
        {
            finished.Add(id);
            if (isFailed) failed.Add(id);
            if (eventStatus == "Đã hủy") cancelled.Add(id);
        }
        if (finished.Count == total)
            return cancelled.Count > 0 ? "Đã hủy" : failed.Count > 0 ? $"Hoàn tất · {failed.Count} lỗi" : "Hoàn tất";
        return eventStatus == "Đang áp dụng" ? $"Đang áp dụng · {finished.Count + 1}/{total}" : $"Đã xử lý · {finished.Count}/{total}";
    }
}

public sealed class MainViewModel : Observable
{
    private readonly List<AppDefinition> catalog;
    private readonly List<WindowsSettingDefinition> windowsCatalog;
    private readonly bool preview;
    private readonly Func<OfficeChoice> chooseOffice;
    private readonly Func<Task<DeviceInfo>> readDeviceInfo;
    private CancellationTokenSource? cancellation;
    public ObservableCollection<AppRow> Apps { get; } = [];
    public ObservableCollection<AppRow> SystemTasks { get; } = [];
    public ObservableCollection<AppRow> WindowsTaskDetails { get; } = [];
    public ObservableCollection<AppRow> ProgressRows { get; } = [];
    public ObservableCollection<WindowsOption> WindowsOptions { get; } = [];
    public IReadOnlyList<SuiteChoice> AppSuites { get; } =
    [
        new("", "Ứng dụng thông thường"),
        new("Office", "Office 2024"),
        new("WPS", "WPS Office")
    ];
    public string Machine => $"{Environment.MachineName}  ·  Windows {WindowsCompatibility.CurrentBuild}  ·  {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
    public string RuntimeLabel
    {
        get
        {
            if (preview) return "PUBLIC · XEM THỬ";
            var version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            return $"Windows desktop · v{version}";
        }
    }
    private int page;
    public int Page
    {
        get => page;
        set
        {
            if (value is < 0 or > 1) return;
            if (Set(ref page, value))
            {
                Raise(nameof(IsInstall)); Raise(nameof(IsDriver));
                if (value == 1) _ = LoadDriverInfoAsync();
            }
        }
    }
    public bool IsInstall => Page == 0;
    public bool IsDriver => Page == 1;
    private bool driverLoaded;
    private bool driverLoading;
    public bool IsDriverLoading { get => driverLoading; private set { if (Set(ref driverLoading, value)) Raise(nameof(DriverSupportLabel)); RefreshDriverCommands(); } }
    private string driverHost = "—";
    public string DriverHost { get => driverHost; private set => Set(ref driverHost, value); }
    private string driverBrand = "—";
    public string DriverBrand { get => driverBrand; private set => Set(ref driverBrand, value); }
    private string driverModel = "—";
    public string DriverModel { get => driverModel; private set => Set(ref driverModel, value); }
    private string driverSerial = "—";
    public string DriverSerial { get => driverSerial; private set { Set(ref driverSerial, value); Raise(nameof(CanCopySerial)); RefreshDriverCommands(); } }
    private string driverUrl = "";
    public string DriverUrl { get => driverUrl; private set { Set(ref driverUrl, value); Raise(nameof(HasDriverUrl)); Raise(nameof(DriverSupportLabel)); RefreshDriverCommands(); } }
    private string driverMessage = "Chọn Driver để đọc thông tin máy.";
    public string DriverMessage { get => driverMessage; private set => Set(ref driverMessage, value); }
    public bool HasDriverUrl => Uri.TryCreate(DriverUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https";
    public string DriverSupportLabel => IsDriverLoading ? "ĐANG ĐỌC THÔNG TIN" : HasDriverUrl ? "ĐÃ XÁC ĐỊNH HÃNG" : "CHƯA CÓ LIÊN KẾT";
    public bool CanCopySerial => !string.IsNullOrWhiteSpace(DriverSerial) && DriverSerial != "—" && DriverSerial != "Không xác định";
    private bool busy;
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) { Raise(nameof(IsIdle)); Raise(nameof(ShowInstallCancel)); Refresh(); } } }
    public bool IsIdle => !IsBusy;
    private bool installRunning;
    public bool IsInstallRunning { get => installRunning; private set { if (Set(ref installRunning, value)) Raise(nameof(ShowInstallCancel)); } }
    public bool ShowInstallCancel => IsBusy && IsInstallRunning;
    private bool hasStarted;
    public bool HasStarted { get => hasStarted; private set { Set(ref hasStarted, value); Raise(nameof(IsReady)); } }
    public bool IsReady => !HasStarted;
    private bool installFinished;
    public bool InstallFinished { get => installFinished; private set { if (Set(ref installFinished, value)) { Raise(nameof(InstallButtonText)); InstallCommand?.Refresh(); } } }
    public string InstallButtonText => InstallFinished ? "Đã hoàn tất" : "Cài đặt";
    private bool isWindowsDetailsVisible;
    public bool IsWindowsDetailsVisible { get => isWindowsDetailsVisible; private set => Set(ref isWindowsDetailsVisible, value); }
    private OfficeChoice officeChoice = OfficeChoice.Office;
    private string summary = "Sẵn sàng cài toàn bộ danh sách đã cấu hình.";
    public string Summary { get => summary; private set => Set(ref summary, value); }
    private string details = "";
    public string Details { get => details; private set => Set(ref details, value); }
    private double overall;
    public double Overall { get => overall; private set => Set(ref overall, value); }
    public string SelectionText => $"{Apps.Count} ứng dụng · {WindowsOptions.Count} thiết lập Windows";
    public RelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CopySerialCommand { get; }
    public RelayCommand OpenDriverSupportCommand { get; }
    public RelayCommand RefreshDriverInfoCommand { get; }
    public RelayCommand ToggleWindowsDetailsCommand { get; }

    public MainViewModel(bool preview = false, Func<OfficeChoice>? chooseOffice = null,
        Func<Task<DeviceInfo>>? readDeviceInfo = null, string? settingsDirectory = null, bool requireSettings = false)
    {
        this.preview = preview;
        this.chooseOffice = chooseOffice ?? OfficeChoiceDialog.Ask;
        this.readDeviceInfo = readDeviceInfo ?? DeviceInfoService.ReadAsync;
        catalog = Catalog.Defaults();
        windowsCatalog = WindowsSettingsCatalog.Defaults();
        if (settingsDirectory != null)
        {
            var store = new SettingsStore(settingsDirectory);
            // Without required packaged settings, an unreadable file falls back to the defaults.
            try { catalog = store.Load(requireSettings); }
            catch (Exception) when (!requireSettings) { }
            try { windowsCatalog = store.LoadWindows(requireSettings); }
            catch (Exception) when (!requireSettings) { }
        }
        InstallCommand = new RelayCommand(async () => await InstallAsync(), () => !IsBusy && !InstallFinished && (Apps.Count > 0 || WindowsOptions.Count > 0));
        CancelCommand = new RelayCommand(() => { cancellation?.Cancel(); Summary = "Đang hủy hàng đợi · chờ bộ cài đang chạy kết thúc…"; }, () => IsInstallRunning && cancellation != null);
        CopySerialCommand = new RelayCommand(CopySerial, () => CanCopySerial);
        OpenDriverSupportCommand = new RelayCommand(OpenDriverSupport, () => HasDriverUrl);
        RefreshDriverInfoCommand = new RelayCommand(() => _ = LoadDriverInfoAsync(true), () => !IsDriverLoading);
        ToggleWindowsDetailsCommand = new RelayCommand(() => IsWindowsDetailsVisible = !IsWindowsDetailsVisible, () => WindowsTaskDetails.Count > 0);
        RebuildWindows();
        RebuildRows();
    }
    private void Refresh() { Raise(nameof(SelectionText)); InstallCommand?.Refresh(); CancelCommand?.Refresh(); }
    private void RefreshDriverCommands() { CopySerialCommand?.Refresh(); OpenDriverSupportCommand?.Refresh(); RefreshDriverInfoCommand?.Refresh(); }
    private async Task LoadDriverInfoAsync(bool force = false)
    {
        if (IsDriverLoading || (driverLoaded && !force)) return;
        IsDriverLoading = true; DriverMessage = "Đang đọc thông tin máy…";
        try
        {
            var info = await readDeviceInfo();
            DriverHost = info.Host; DriverBrand = info.Brand; DriverModel = info.Model; DriverSerial = info.Serial; DriverUrl = info.DriverUrl;
            DriverMessage = HasDriverUrl ? "Đã nhận diện hãng. Mở trang chính thức để tải driver theo model hoặc serial." : "Chưa có trang hỗ trợ phù hợp cho hãng này.";
            driverLoaded = true;
        }
        catch (Exception ex) { DriverMessage = "Không đọc được thông tin máy: " + ex.Message; }
        finally { IsDriverLoading = false; }
    }
    private void CopySerial()
    {
        try { Clipboard.SetText(DriverSerial); DriverMessage = "Đã sao chép Serial."; }
        catch (Exception ex) { DriverMessage = "Không sao chép được Serial: " + ex.Message; }
    }
    private void OpenDriverSupport()
    {
        if (!HasDriverUrl) return;
        try { Process.Start(new ProcessStartInfo(DriverUrl) { UseShellExecute = true }); }
        catch (Exception ex) { DriverMessage = "Không mở được trang hỗ trợ: " + ex.Message; }
    }
    private void RebuildWindows()
    {
        WindowsOptions.Clear();
        foreach (var definition in windowsCatalog.Where(definition => !IsDebloat(definition)))
        {
            var option = new WindowsOption(definition);
            WindowsOptions.Add(option);
        }
        Refresh();
    }
    public static bool IsDebloat(WindowsSettingDefinition definition) =>
        definition.Id.Equals("Debloat", StringComparison.OrdinalIgnoreCase) ||
        definition.Action.Equals("Debloat", StringComparison.OrdinalIgnoreCase);

    private void RebuildRows()
    {
        Apps.Clear();
        SystemTasks.Clear();
        WindowsTaskDetails.Clear();
        ProgressRows.Clear();
        IsWindowsDetailsVisible = false;
        ToggleWindowsDetailsCommand.Refresh();
        var suite = officeChoice == OfficeChoice.Wps ? "WPS" : "Office";
        foreach (var app in catalog.Where(a => a.Suite.Length == 0 || a.Suite == suite))
        {
            var row = new AppRow(app);
            Apps.Add(row);
            ProgressRows.Add(row);
        }
        HasStarted = false;
        Overall = 0;
        Summary = "Sẵn sàng cài toàn bộ danh sách đã cấu hình.";
        Refresh();
    }
    private void Log(string line) => Details += $"[{DateTime.Now:HH:mm:ss}] {line}\n";
    private async Task InstallAsync()
    {
        var options = WindowsOptions.Select(o => o.Definition).ToList();
        var choice = chooseOffice();
        if (choice == OfficeChoice.Cancel) return;
        if (!preview)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show("Hãy chạy bằng bootstrap hoặc mở MiniApps với quyền Administrator. Chưa có tác vụ nào được chạy.", "MiniApps", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        officeChoice = choice;
        RebuildRows();
        var selected = Apps.Select(a => a.Definition).ToList();
        using var deploymentLock = new Mutex(false, "Global\\MiniApps.Deployment");
        bool acquired;
        try { acquired = deploymentLock.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { MessageBox.Show("Một cửa sổ MiniApps khác đang cài đặt. Hãy chờ lượt đó kết thúc.", "MiniApps"); return; }
        IsBusy = true; IsInstallRunning = true; Details = ""; Overall = 0;
        HasStarted = true;
        if (options.Count > 0)
        {
            var windowsRow = new AppRow(new AppDefinition { Id = "windows:summary", Name = "Windows Setting" }) { Status = $"Chờ áp dụng · 0/{options.Count}" };
            SystemTasks.Add(windowsRow);
            ProgressRows.Add(windowsRow);
            foreach (var option in options)
                WindowsTaskDetails.Add(new AppRow(new AppDefinition { Id = option.TaskId, Name = option.Name }) { Status = "Chờ áp dụng" });
            ToggleWindowsDetailsCommand.Refresh();
        }
        foreach (var row in Apps) { row.Status = "Chờ tải"; row.Progress = 0; }
        cancellation = new CancellationTokenSource();
        var total = selected.Count + options.Count;
        var finished = new HashSet<string>(); var failed = new HashSet<string>();
        var smartSkipped = new HashSet<string>(); var unverified = new HashSet<string>();
        var windowsIds = options.Select(o => o.TaskId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var windowsNames = options.ToDictionary(o => o.TaskId, o => o.Name, StringComparer.OrdinalIgnoreCase);
        var windowsProgress = new WindowsProgressTracker(options.Count);
        var events = new Progress<DeploymentEvent>(e =>
        {
            var isWindows = windowsIds.Contains(e.Id);
            var row = isWindows ? SystemTasks.FirstOrDefault() : Apps.FirstOrDefault(a => a.Definition.Id == e.Id);
            if (isWindows)
            {
                var detailRow = WindowsTaskDetails.FirstOrDefault(a => a.Definition.Id == e.Id);
                if (detailRow != null) { detailRow.Status = e.Status; detailRow.Progress = e.Progress; }
                if (row != null)
                {
                    row.Status = windowsProgress.Update(e.Id, e.Status, e.Finished, e.Failed);
                    row.Progress = windowsProgress.Progress;
                }
            }
            else if (row != null) { row.Status = e.Status; row.Progress = e.Progress; }
            if (e.Finished)
            {
                finished.Add(e.Id); if (e.Failed) failed.Add(e.Id);
                if (!isWindows && e.Status.StartsWith("Đã cài", StringComparison.Ordinal)) smartSkipped.Add(e.Id);
                if (!isWindows && e.Status.StartsWith("Không xác minh được", StringComparison.Ordinal)) unverified.Add(e.Id);
                Log($"{(isWindows ? windowsNames[e.Id] : row?.Name ?? e.Id)}: {e.Status}");
            }
            Overall = total == 0 ? 0 : finished.Count * 100d / total;
            Summary = $"Đã xử lý {finished.Count}/{total} · {failed.Count} lỗi";
        });
        var workDir = Path.Combine(Path.GetTempPath(), "MiniApps", "work-" + Guid.NewGuid().ToString("N"));
        var session = Environment.GetEnvironmentVariable("MINIAPPS_SESSION");
        var marker = !preview && !string.IsNullOrEmpty(session) ? Path.Combine(session, "installing") : null;
        var runCompleted = false;
        try
        {
            if (marker != null) File.WriteAllText(marker, "Do not clean until process tree exits.");
            Summary = preview ? "Đang mô phỏng · không tải hoặc chạy bộ cài" : "Đang chuẩn bị cài đặt…";
            if (preview)
            {
                var previewApps = selected.Select(async app =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ((IProgress<DeploymentEvent>)events).Report(new(app.Id, "Đang tải · mô phỏng", 45));
                    await Task.Delay(180, cancellation.Token);
                    ((IProgress<DeploymentEvent>)events).Report(new(app.Id, "Đang cài · mô phỏng", 100));
                    await Task.Delay(700);
                    ((IProgress<DeploymentEvent>)events).Report(new(app.Id, "Hoàn tất · mô phỏng", 100, true));
                });
                var previewSettings = options.Select(async option =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ((IProgress<DeploymentEvent>)events).Report(new(option.TaskId, "Đang áp dụng", 0));
                    await Task.Delay(500, cancellation.Token);
                    ((IProgress<DeploymentEvent>)events).Report(new(option.TaskId, "Hoàn tất · mô phỏng", 100, true));
                });
                await Task.WhenAll(previewApps.Concat(previewSettings));
            }
            else await new DeploymentService().RunAsync(selected, options, workDir, events, new Progress<string>(Log), cancellation.Token);
            // Drain progress callbacks before composing the final summary.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Summary = cancellation.IsCancellationRequested ? "Đã dừng · bộ cài đang chạy đã kết thúc an toàn."
                : $"Hoàn tất · {finished.Count}/{total} tác vụ · bỏ qua {smartSkipped.Count} đã cài · chưa xác minh {unverified.Count} · {failed.Count} lỗi";
            runCompleted = !cancellation.IsCancellationRequested && failed.Count == 0;
        }
        catch (OperationCanceledException) { Summary = "Đã hủy lượt chạy."; }
        catch (Exception ex) { Log(ex.ToString()); Summary = "Có lỗi trong quá trình cài đặt."; }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); }
            catch (Exception ex) { Log("Chưa dọn hết bộ cài tạm: " + ex.Message); }
            cancellation.Dispose(); cancellation = null; InstallFinished = runCompleted; IsInstallRunning = false; IsBusy = false;
            if (marker != null) { try { File.Delete(marker); } catch (IOException) { } }
            deploymentLock.ReleaseMutex();
        }
    }
}
