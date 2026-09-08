using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
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

internal sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
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
    private readonly object optimizeTasksLock = new();
    private readonly SettingsStore store;
    private List<AppDefinition> catalog;
    private List<WindowsSettingDefinition> windowsCatalog;
    private readonly bool preview;
    private readonly bool settingsWritable;
    private readonly Func<OfficeChoice> chooseOffice;
    private readonly Func<string, bool> confirmDelete;
    private readonly Func<Task<DeviceInfo>> readDeviceInfo;
    private CancellationTokenSource? cancellation;
#if NET48
    private readonly IOptimizeService optimizeService;
#endif
    public ObservableCollection<AppRow> Apps { get; } = [];
    public ObservableCollection<AppRow> SystemTasks { get; } = [];
    public ObservableCollection<AppRow> WindowsTaskDetails { get; } = [];
    public ObservableCollection<AppRow> ProgressRows { get; } = [];
    public ObservableCollection<AppDefinition> EditableApps { get; } = [];
    public ObservableCollection<WindowsOption> WindowsOptions { get; } = [];
    public ObservableCollection<WindowsSettingDefinition> EditableWindows { get; } = [];
    public ObservableCollection<OptimizePreviewRow> OptimizeTasks { get; } =
    [
        new("remove-apps", "Gỡ ứng dụng thừa", "Xem trước việc gỡ các ứng dụng cài sẵn đã được MiniApps duyệt."),
        new("privacy", "Quyền riêng tư & quảng cáo", "Xem trước các tinh chỉnh giảm đề xuất, quảng cáo và nội dung không cần thiết."),
        new("copilot", "Copilot & AI", "Xem trước việc tắt và gỡ Copilot cùng các thành phần AI đã thống nhất."),
        new("interface", "Giao diện Windows", "Xem trước các tinh chỉnh giao diện gọn và thuận tiện hơn.")
    ];
    public ICollectionView OptimizeTaskGroups { get; }
    public IReadOnlyList<SuiteChoice> AppSuites { get; } =
    [
        new("", "Ứng dụng thông thường"),
        new("Office", "Office 2024"),
        new("WPS", "WPS Office")
    ];
    public ICollectionView FilteredApps { get; }
    public ICollectionView FilteredWindows { get; }
    public bool IsDeveloperEdition { get; }
    public bool CanAccessOptimize => IsDeveloperEdition;
    public bool ShowOptimizeTab => false;
    private int settingsTab;
    public int SettingsTab { get => settingsTab; set { if (Set(ref settingsTab, value)) RefreshSettingsState(); } }
    public string Machine => $"{Environment.MachineName}  ·  Windows {WindowsCompatibility.CurrentBuild}  ·  {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
    public string RuntimeLabel
    {
        get
        {
            if (preview) return IsDeveloperEdition ? "DEVELOPER · XEM THỬ" : "PUBLIC · XEM THỬ";
            var version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            return IsDeveloperEdition ? $"DEVELOPER · v{version}" : $"Windows desktop · v{version}";
        }
    }
    private int page;
    public int Page
    {
        get => page;
        set
        {
            if (value is < 0 or > 3) return;
            if (!IsDeveloperEdition && value is not (0 or 2)) return;
            if (value == 3 && IsBusy) return;
            if (Set(ref page, value))
            {
                Raise(nameof(IsInstall)); Raise(nameof(IsOptimize)); Raise(nameof(IsDriver)); Raise(nameof(IsSetting));
                if (value == 2) _ = LoadDriverInfoAsync();
            }
        }
    }
    public bool IsInstall => Page == 0;
    public bool IsOptimize => CanAccessOptimize && Page == 1;
    public bool IsDriver => Page == 2;
    public bool IsSetting => IsDeveloperEdition && Page == 3;
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
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) { Raise(nameof(IsIdle)); Raise(nameof(ShowInstallCancel)); Raise(nameof(InstallBlockingMessage)); Raise(nameof(OptimizeBlockingMessage)); Refresh(); } } }
    public bool IsIdle => !IsBusy;
    private bool installRunning;
    public bool IsInstallRunning { get => installRunning; private set { if (Set(ref installRunning, value)) { Raise(nameof(ShowInstallCancel)); Raise(nameof(InstallBlockingMessage)); Raise(nameof(OptimizeBlockingMessage)); } } }
    private bool optimizeRunning;
    public bool IsOptimizeRunning { get => optimizeRunning; private set { if (Set(ref optimizeRunning, value)) { Raise(nameof(ShowInstallCancel)); Raise(nameof(InstallBlockingMessage)); Raise(nameof(OptimizeBlockingMessage)); Raise(nameof(OptimizeIndeterminate)); } } }
    public bool ShowInstallCancel => IsBusy && IsInstallRunning;
    public string InstallBlockingMessage => IsOptimizeRunning ? "Optimize Windows đang chạy. Cài đặt sẽ khả dụng sau khi tối ưu hoàn tất." : "";
    public string OptimizeBlockingMessage => IsInstallRunning ? "Cài đặt ứng dụng đang chạy. Optimize sẽ khả dụng sau khi cài đặt hoàn tất." : "";
    private bool hasStarted;
    public bool HasStarted { get => hasStarted; private set { Set(ref hasStarted, value); Raise(nameof(IsReady)); } }
    public bool IsReady => !HasStarted;
    private bool installFinished;
    public bool InstallFinished { get => installFinished; private set { if (Set(ref installFinished, value)) { Raise(nameof(InstallButtonText)); InstallCommand?.Refresh(); } } }
    public string InstallButtonText => InstallFinished ? "Đã hoàn tất" : "Cài đặt";
    private bool optimizeStarted;
    public bool OptimizeStarted { get => optimizeStarted; private set => Set(ref optimizeStarted, value); }
    private bool optimizeFinished;
    public bool OptimizeFinished { get => optimizeFinished; private set { if (Set(ref optimizeFinished, value)) { Raise(nameof(OptimizeButtonText)); OptimizeCommand?.Refresh(); } } }
    private OptimizeStage optimizeStage = OptimizeStage.Ready;
    public OptimizeStage OptimizeStage { get => optimizeStage; private set { if (Set(ref optimizeStage, value)) { Raise(nameof(OptimizeStageText)); Raise(nameof(OptimizeIndeterminate)); Raise(nameof(OptimizeHasStatus)); } } }
    public string OptimizeStageText => OptimizeStage switch
    {
        OptimizeStage.Preparing => "ĐANG CHUẨN BỊ",
        OptimizeStage.Applying => "ĐANG ÁP DỤNG",
#if NET48
        OptimizeStage.Overdue => "GỠ PACKAGE QUÁ THỜI GIAN",
#endif
        OptimizeStage.Completed => preview ? "ĐÃ XEM TRƯỚC" : "HOÀN TẤT",
        OptimizeStage.Error => "CÓ LỖI",
        _ => "SẴN SÀNG"
    };
    public string OptimizeButtonText => OptimizeFinished ? (preview ? "Đã xem trước" : "Đã tối ưu") : "Tối ưu Windows";
    public bool OptimizeHasStatus => OptimizeStarted || OptimizeStage == OptimizeStage.Error;
    private string optimizeLogDirectory = "";
    public string OptimizeLogDirectory { get => optimizeLogDirectory; private set { if (Set(ref optimizeLogDirectory, value)) { Raise(nameof(HasOptimizeLog)); OpenOptimizeLogCommand?.Refresh(); } } }
    public bool HasOptimizeLog => !string.IsNullOrWhiteSpace(OptimizeLogDirectory) && Directory.Exists(OptimizeLogDirectory);
    private string optimizeSummary = "Sẵn sàng tối ưu Windows theo cấu hình Default.";
    public string OptimizeSummary { get => optimizeSummary; private set => Set(ref optimizeSummary, value); }
    private double optimizeOverall;
    public double OptimizeOverall { get => optimizeOverall; private set => Set(ref optimizeOverall, value); }
    private bool isWindowsDetailsVisible;
    public bool IsWindowsDetailsVisible { get => isWindowsDetailsVisible; private set => Set(ref isWindowsDetailsVisible, value); }
    private OfficeChoice officeChoice = OfficeChoice.Office;
    private string summary = "Sẵn sàng cài toàn bộ danh sách đã cấu hình.";
    public string Summary { get => summary; private set => Set(ref summary, value); }
    private string details = "";
    public string Details { get => details; private set => Set(ref details, value); }
    private double overall;
    public double Overall { get => overall; private set => Set(ref overall, value); }
    private string settingsMessage = "Chỉ dùng URL và script tin cậy. Thêm, sửa, xóa chưa có hiệu lực cho đến khi Lưu phần đang mở.";
    public string SettingsMessage { get => settingsMessage; private set => Set(ref settingsMessage, value); }
    private string appSearch = "";
    public string AppSearch { get => appSearch; set { if (Set(ref appSearch, value)) { FilteredApps.Refresh(); SelectVisibleApp(); Raise(nameof(FilteredAppCount)); } } }
    private string windowsSearch = "";
    public string WindowsSearch { get => windowsSearch; set { if (Set(ref windowsSearch, value)) { FilteredWindows.Refresh(); SelectVisibleWindows(); Raise(nameof(FilteredWindowsCount)); } } }
    private bool appsDirty;
    public bool AppsDirty { get => appsDirty; private set => Set(ref appsDirty, value); }
    private bool windowsDirty;
    public bool WindowsDirty { get => windowsDirty; private set => Set(ref windowsDirty, value); }
    public bool IsCurrentDirty => SettingsTab == 0 ? AppsDirty : WindowsDirty;
    public string SaveStatus => IsCurrentDirty ? "Chưa lưu thay đổi" : "Đã lưu";
    private string currentValidationError = "";
    public string CurrentValidationError { get => currentValidationError; private set { Set(ref currentValidationError, value); Raise(nameof(HasValidationError)); } }
    public bool HasValidationError => CurrentValidationError.Length > 0;
    public int AppCount => EditableApps.Count;
    public int WindowsCount => EditableWindows.Count;
    public int FilteredAppCount => FilteredApps.Cast<object>().Count();
    public int FilteredWindowsCount => FilteredWindows.Cast<object>().Count();
    private AppDefinition? selectedApp;
    public AppDefinition? SelectedApp { get => selectedApp; set { if (Set(ref selectedApp, value)) { Raise(nameof(HasSelectedApp)); Refresh(); } } }
    public bool HasSelectedApp => SelectedApp != null;
    private WindowsSettingDefinition? selectedWindows;
    public WindowsSettingDefinition? SelectedWindows { get => selectedWindows; set { if (Set(ref selectedWindows, value)) { Raise(nameof(HasSelectedWindows)); Raise(nameof(IsSelectedDebloat)); Refresh(); } } }
    public bool HasSelectedWindows => SelectedWindows != null;
    public bool IsSelectedDebloat => SelectedWindows != null && IsDebloat(SelectedWindows);
    public string SelectionText => $"{Apps.Count} ứng dụng · {WindowsOptions.Count} thiết lập Windows";
    public RelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand AddWindowsCommand { get; }
    public RelayCommand DeleteWindowsCommand { get; }
    public RelayCommand CopySerialCommand { get; }
    public RelayCommand OpenDriverSupportCommand { get; }
    public RelayCommand RefreshDriverInfoCommand { get; }
    public RelayCommand ToggleWindowsDetailsCommand { get; }
    public RelayCommand OptimizeCommand { get; }
    public RelayCommand OpenOptimizeLogCommand { get; }
    public string OptimizeNotice =>
#if NET48
        preview ? "Preview — không thay đổi hệ thống" : "MiniApps Debloat · Windows 11";
#else
        "Preview — không thay đổi hệ thống";
#endif
    public string OptimizeDescription =>
#if NET48
        "Áp dụng engine MiniApps dựa trên cấu hình Default của Win11Debloat: gỡ ứng dụng mặc định; giảm telemetry và quảng cáo; tắt các tính năng AI đã chọn; tinh chỉnh taskbar và Explorer. Đăng xuất hoặc khởi động lại sau khi hoàn tất.";
#else
        "Bản xem thử chỉ mô phỏng trạng thái; không thay đổi hệ thống.";
#endif
    public bool OptimizeIndeterminate => IsOptimizeRunning && OptimizeStage == OptimizeStage.Applying;
#if NET48
    public bool UseOptimizeTaskTimeline => true;
#else
    public bool UseOptimizeTaskTimeline => false;
#endif
    public bool UseLegacyOptimizeCards => !UseOptimizeTaskTimeline;

    public MainViewModel(bool preview = false, Func<OfficeChoice>? chooseOffice = null, Func<string, bool>? confirmDelete = null,
        Func<Task<DeviceInfo>>? readDeviceInfo = null, bool? developerEdition = null, string? settingsDirectory = null,
        bool settingsWritable = false, bool requireSettings = false
#if NET48
        , IOptimizeService? optimizeService = null
#endif
        )
    {
        this.preview = preview;
#if NET48
        this.optimizeService = optimizeService ?? (preview ? new OptimizePreviewService() : new OptimizeService());
        OptimizeTasks.Clear();
        foreach (var task in OptimizeTaskCatalog.Defaults()) OptimizeTasks.Add(task);
#endif
        BindingOperations.EnableCollectionSynchronization(OptimizeTasks, optimizeTasksLock);
        OptimizeTaskGroups = new ListCollectionView(OptimizeTasks);
#if NET48
        OptimizeTaskGroups.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OptimizePreviewRow.Category)));
#endif
        IsDeveloperEdition = developerEdition ?? BuildEdition.IsDeveloper;
        this.settingsWritable = IsDeveloperEdition && (settingsWritable || !preview);
        this.chooseOffice = chooseOffice ?? OfficeChoiceDialog.Ask;
        this.confirmDelete = confirmDelete ?? (message => MessageBox.Show(message, "Xóa khỏi danh sách", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        this.readDeviceInfo = readDeviceInfo ?? DeviceInfoService.ReadAsync;
        store = new SettingsStore(settingsDirectory ?? (preview ? Path.Combine(Path.GetTempPath(), "MiniApps-preview-settings") : null));
        FilteredApps = CollectionViewSource.GetDefaultView(EditableApps);
        FilteredWindows = CollectionViewSource.GetDefaultView(EditableWindows);
        FilteredApps.Filter = item => MatchesApp((AppDefinition)item);
        FilteredWindows.Filter = item => MatchesWindows((WindowsSettingDefinition)item);
        EditableApps.CollectionChanged += OnAppsChanged;
        EditableWindows.CollectionChanged += OnWindowsChanged;
        catalog = Catalog.Defaults();
        windowsCatalog = WindowsSettingsCatalog.Defaults();
        var loadSettings = settingsDirectory != null || (!preview && IsDeveloperEdition);
        try { if (loadSettings) catalog = store.Load(requireSettings); }
        catch (Exception ex) { if (requireSettings) throw; SettingsMessage = $"Không đọc được cấu hình: {ex.Message} Đang dùng mặc định; file cũ chưa bị ghi đè."; }
        try { if (loadSettings) windowsCatalog = store.LoadWindows(requireSettings); }
        catch (Exception ex) { if (requireSettings) throw; SettingsMessage = $"Không đọc được thiết lập: {ex.Message} Đang dùng mặc định; file cũ chưa bị ghi đè."; }
        InstallCommand = new RelayCommand(async () => await InstallAsync(), () => !IsBusy && !InstallFinished && (Apps.Count > 0 || WindowsOptions.Count > 0));
        CancelCommand = new RelayCommand(() => { cancellation?.Cancel(); Summary = "Đang hủy hàng đợi · chờ bộ cài đang chạy kết thúc…"; }, () => IsInstallRunning && cancellation != null);
        SaveCommand = new RelayCommand(Save, () => IsDeveloperEdition && !IsBusy && IsCurrentDirty && !HasValidationError);
        DiscardCommand = new RelayCommand(DiscardCurrent, () => IsDeveloperEdition && !IsBusy && IsCurrentDirty);
        AddCommand = new RelayCommand(() => { AppSearch = ""; var item = new AppDefinition { Name = "Ứng dụng mới" }; EditableApps.Add(item); SelectedApp = item; }, () => IsDeveloperEdition && !IsBusy);
        DeleteCommand = new RelayCommand(DeleteApp, () => IsDeveloperEdition && !IsBusy && SelectedApp != null);
        AddWindowsCommand = new RelayCommand(() => { WindowsSearch = ""; var item = new WindowsSettingDefinition { Name = "Thiết lập mới" }; EditableWindows.Add(item); SelectedWindows = item; }, () => IsDeveloperEdition && !IsBusy);
        DeleteWindowsCommand = new RelayCommand(DeleteWindows, () => IsDeveloperEdition && !IsBusy && SelectedWindows != null);
        CopySerialCommand = new RelayCommand(CopySerial, () => CanCopySerial);
        OpenDriverSupportCommand = new RelayCommand(OpenDriverSupport, () => HasDriverUrl);
        RefreshDriverInfoCommand = new RelayCommand(() => _ = LoadDriverInfoAsync(true), () => !IsDriverLoading);
        ToggleWindowsDetailsCommand = new RelayCommand(() => IsWindowsDetailsVisible = !IsWindowsDetailsVisible, () => WindowsTaskDetails.Count > 0);
        OptimizeCommand = new RelayCommand(async () => await RunOptimizeAsync(), () => CanAccessOptimize && !IsBusy && !OptimizeFinished);
        OpenOptimizeLogCommand = new RelayCommand(OpenOptimizeLog, () => CanAccessOptimize && HasOptimizeLog);
        RebuildWindows();
        RebuildRows();
        if (IsDeveloperEdition) CopyToEditor();
    }
    private void Refresh() { Raise(nameof(SelectionText)); InstallCommand?.Refresh(); CancelCommand?.Refresh(); SaveCommand?.Refresh(); DiscardCommand?.Refresh(); AddCommand?.Refresh(); DeleteCommand?.Refresh(); AddWindowsCommand?.Refresh(); DeleteWindowsCommand?.Refresh(); OptimizeCommand?.Refresh(); OpenOptimizeLogCommand?.Refresh(); }
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

    private async Task RunOptimizeAsync()
    {
        if (IsBusy) return;
#if NET48
        ResetOptimizeRun();
        IsBusy = true; IsOptimizeRunning = true; OptimizeStarted = true; OptimizeOverall = 0;
        OptimizeStage = OptimizeStage.Ready;
        OptimizeSummary = preview ? "Đang xem trước quy trình · không thay đổi hệ thống" : "Đang chuẩn bị tối ưu Windows…";
        foreach (var row in OptimizeTasks) { row.State = OptimizeTaskState.Waiting; row.Status = "Đang chờ"; row.Progress = 0; }
        var dispatcher = Application.Current?.Dispatcher;
        OptimizeResult? result = null;
        Exception? failure = null;
        try
        {
            var progress = new DirectProgress<OptimizeProgress>(item => DispatchOptimizeProgress(dispatcher, item));
            result = await optimizeService.RunAsync(progress, CancellationToken.None);
        }
        catch (Exception ex) { failure = ex; }
        Action finish = () =>
        {
            if (failure != null)
            {
                Log(failure.ToString()); OptimizeStage = OptimizeStage.Error; OptimizeSummary = "Tối ưu chưa hoàn tất: " + failure.Message;
                MarkUnconfirmedOptimizeTasks();
                OptimizeSummary = AppendOptimizeCounts(OptimizeSummary);
                UpdateOptimizeOverall();
            }
            else if (result != null)
            {
                OptimizeLogDirectory = result.LogDirectory;
                OptimizeFinished = result.Succeeded;
                OptimizeStage = result.Succeeded ? OptimizeStage.Completed :
                    OptimizeTasks.Any(task => task.State == OptimizeTaskState.Overdue) ? OptimizeStage.Overdue : OptimizeStage.Error;
                MarkUnconfirmedOptimizeTasks();
                UpdateOptimizeOverall();
                OptimizeSummary = AppendOptimizeCounts(result.Message);
            }
            IsOptimizeRunning = false; IsBusy = false;
        };
        if (dispatcher != null && !dispatcher.CheckAccess()) await dispatcher.InvokeAsync(finish);
        else finish();
#else
        IsBusy = true;
        OptimizeStarted = true;
        OptimizeOverall = 0;
        OptimizeSummary = "Đang mô phỏng · không thay đổi hệ thống";
        foreach (var task in OptimizeTasks) { task.Status = "Đang chờ"; task.Progress = 0; }
        try
        {
            var completed = 0;
            var operations = OptimizeTasks.Select(async task =>
            {
                task.Status = "Đang phân tích · mô phỏng"; task.Progress = 35;
                await Task.Delay(180);
                task.Status = "Đang chuẩn bị · mô phỏng"; task.Progress = 72;
                await Task.Delay(180);
                task.Status = "Hoàn tất · mô phỏng"; task.Progress = 100;
                var completedCount = Interlocked.Increment(ref completed);
                OptimizeOverall = completedCount * 100d / OptimizeTasks.Count;
                OptimizeSummary = $"Đã mô phỏng {completedCount}/{OptimizeTasks.Count} nhóm · không có thay đổi nào được áp dụng";
            }).ToArray();
            await Task.WhenAll(operations);
            OptimizeOverall = 100;
        }
        finally { IsBusy = false; }
#endif
    }

#if NET48
    private void UpdateOptimizeProgress(OptimizeProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.LogDirectory)) OptimizeLogDirectory = progress.LogDirectory;
        if (progress.Task is { } task) UpdateOptimizeTask(task);
        if (progress.Stage == OptimizeStage.Ready) return;
        OptimizeStage = progress.Stage;
        if (!string.IsNullOrWhiteSpace(progress.Message)) OptimizeSummary = progress.Message;
    }

    private void UpdateOptimizeTask(OptimizeTaskUpdate update)
    {
        var row = OptimizeTasks.FirstOrDefault(item => item.Id.Equals(update.Id, StringComparison.OrdinalIgnoreCase));
        if (row == null) return;
        row.State = update.Event switch
        {
            OptimizeTaskEvent.Queued => OptimizeTaskState.Waiting,
            OptimizeTaskEvent.Start => OptimizeTaskState.Running,
            OptimizeTaskEvent.Done => OptimizeTaskState.Succeeded,
            OptimizeTaskEvent.Skip => OptimizeTaskState.Skipped,
            OptimizeTaskEvent.Error => OptimizeTaskState.Failed,
            OptimizeTaskEvent.Overdue => OptimizeTaskState.Overdue,
            _ => row.State
        };
        row.Status = !string.IsNullOrWhiteSpace(update.Message) ? update.Message : update.Event switch
        {
            OptimizeTaskEvent.Queued => "Đang chờ",
            OptimizeTaskEvent.Start => "Đang chạy",
            OptimizeTaskEvent.Done => preview ? "Hoàn tất · mô phỏng" : "Đã áp dụng",
            OptimizeTaskEvent.Skip => "Đã bỏ qua",
            OptimizeTaskEvent.Error => "Có lỗi",
            OptimizeTaskEvent.Overdue => "Quá thời gian · Windows vẫn đang xử lý",
            _ => row.Status
        };
        row.Progress = update.Event is OptimizeTaskEvent.Done or OptimizeTaskEvent.Skip or OptimizeTaskEvent.Error ? 100 : 0;
        var oldIndex = OptimizeTasks.IndexOf(row);
        if (update.Event == OptimizeTaskEvent.Start && oldIndex > 0) OptimizeTasks.Move(oldIndex, 0);
        else if ((update.Event is OptimizeTaskEvent.Done or OptimizeTaskEvent.Skip or OptimizeTaskEvent.Error) && oldIndex >= 0 && oldIndex < OptimizeTasks.Count - 1)
            OptimizeTasks.Move(oldIndex, OptimizeTasks.Count - 1);
        UpdateOptimizeOverall();
    }

    private void MarkUnconfirmedOptimizeTasks()
    {
        foreach (var row in OptimizeTasks.Where(item => item.State is OptimizeTaskState.Ready or OptimizeTaskState.Waiting or OptimizeTaskState.Running))
        {
            row.State = OptimizeTaskState.NotConfirmed;
            row.Status = "Không có kết quả xác nhận";
            row.Progress = 0;
        }
    }

    private void UpdateOptimizeOverall()
    {
        var terminal = OptimizeTasks.Count(item => item.State is OptimizeTaskState.Succeeded or OptimizeTaskState.Skipped or OptimizeTaskState.Failed);
        OptimizeOverall = OptimizeTasks.Count == 0 ? 0 : terminal * 100d / OptimizeTasks.Count;
    }

    private void ResetOptimizeRun()
    {
        OptimizeTasks.Clear();
        foreach (var task in OptimizeTaskCatalog.Defaults()) OptimizeTasks.Add(task);
        OptimizeLogDirectory = "";
        OptimizeFinished = false;
        OptimizeOverall = 0;
    }

    private string AppendOptimizeCounts(string message)
    {
        if (message.IndexOf("Thành công ", StringComparison.Ordinal) >= 0) return message;
        var succeeded = OptimizeTasks.Count(item => item.State == OptimizeTaskState.Succeeded);
        var skipped = OptimizeTasks.Count(item => item.State == OptimizeTaskState.Skipped);
        var failed = OptimizeTasks.Count(item => item.State == OptimizeTaskState.Failed);
        var overdue = OptimizeTasks.Count(item => item.State == OptimizeTaskState.Overdue);
        var notRun = OptimizeTasks.Count - succeeded - skipped - failed - overdue;
        return $"{message.TrimEnd()} Thành công {succeeded} · Bỏ qua {skipped} · Lỗi {failed} · Quá hạn {overdue} · Chưa chạy {notRun}.";
    }

    private void DispatchOptimizeProgress(System.Windows.Threading.Dispatcher? dispatcher, OptimizeProgress progress)
    {
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(() => UpdateOptimizeProgress(progress));
        else UpdateOptimizeProgress(progress);
    }

    private void OpenOptimizeLog()
    {
        if (!HasOptimizeLog) return;
        try { Process.Start(new ProcessStartInfo(OptimizeLogDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { OptimizeSummary = "Không mở được nhật ký: " + ex.Message; }
    }
#else
    private void OpenOptimizeLog() { }
#endif
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
    private void CopyToEditor()
    {
        EditableApps.Clear();
        foreach (var item in JsonSerializer.Deserialize<List<AppDefinition>>(JsonSerializer.Serialize(catalog))!) EditableApps.Add(item);
        SelectedApp = EditableApps.FirstOrDefault();
        EditableWindows.Clear();
        foreach (var item in JsonSerializer.Deserialize<List<WindowsSettingDefinition>>(JsonSerializer.Serialize(windowsCatalog))!) EditableWindows.Add(item);
        SelectedWindows = EditableWindows.FirstOrDefault();
        RefreshSettingsState();
    }
    private bool MatchesApp(AppDefinition item)
    {
        var query = AppSearch.Trim();
        return query.Length == 0 || new[] { item.Name, item.Url, item.Suite }.Any(value => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private bool MatchesWindows(WindowsSettingDefinition item)
    {
        var query = WindowsSearch.Trim();
        return query.Length == 0 || new[] { item.Name, item.Description, item.Action }.Any(value => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private void SelectVisibleApp()
    {
        if (SelectedApp != null && FilteredApps.Cast<object>().Contains(SelectedApp)) return;
        SelectedApp = FilteredApps.Cast<AppDefinition>().FirstOrDefault();
    }
    private void SelectVisibleWindows()
    {
        if (SelectedWindows != null && FilteredWindows.Cast<object>().Contains(SelectedWindows)) return;
        SelectedWindows = FilteredWindows.Cast<WindowsSettingDefinition>().FirstOrDefault();
    }
    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (AppDefinition item in e.OldItems) item.PropertyChanged -= OnEditorItemChanged;
        if (e.NewItems != null) foreach (AppDefinition item in e.NewItems) item.PropertyChanged += OnEditorItemChanged;
        FilteredApps.Refresh();
        Raise(nameof(AppCount)); Raise(nameof(FilteredAppCount));
        RefreshSettingsState();
    }
    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (WindowsSettingDefinition item in e.OldItems) item.PropertyChanged -= OnEditorItemChanged;
        if (e.NewItems != null) foreach (WindowsSettingDefinition item in e.NewItems) item.PropertyChanged += OnEditorItemChanged;
        FilteredWindows.Refresh();
        Raise(nameof(WindowsCount)); Raise(nameof(FilteredWindowsCount));
        RefreshSettingsState();
    }
    private void OnEditorItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AppDefinition) { FilteredApps.Refresh(); Raise(nameof(FilteredAppCount)); }
        else { FilteredWindows.Refresh(); Raise(nameof(FilteredWindowsCount)); Raise(nameof(IsSelectedDebloat)); }
        RefreshSettingsState();
    }
    private static string Snapshot<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items);
    private void RefreshSettingsState()
    {
        AppsDirty = Snapshot(EditableApps) != Snapshot(catalog);
        WindowsDirty = Snapshot(EditableWindows) != Snapshot(windowsCatalog);
        CurrentValidationError = ValidateCurrent();
        Raise(nameof(IsCurrentDirty)); Raise(nameof(SaveStatus));
        Refresh();
    }
    private string ValidateCurrent()
    {
        try
        {
            if (SettingsTab == 0) Catalog.Validate(EditableApps);
            else WindowsSettingsCatalog.Validate(EditableWindows);
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }
    private void DiscardCurrent()
    {
        if (SettingsTab == 0)
        {
            EditableApps.Clear();
            foreach (var item in JsonSerializer.Deserialize<List<AppDefinition>>(JsonSerializer.Serialize(catalog))!) EditableApps.Add(item);
            SelectedApp = EditableApps.FirstOrDefault();
            AppSearch = "";
        }
        else
        {
            EditableWindows.Clear();
            foreach (var item in JsonSerializer.Deserialize<List<WindowsSettingDefinition>>(JsonSerializer.Serialize(windowsCatalog))!) EditableWindows.Add(item);
            SelectedWindows = EditableWindows.FirstOrDefault();
            WindowsSearch = "";
        }
        SettingsMessage = "Đã hủy thay đổi trong phần đang mở.";
        RefreshSettingsState();
    }
    private void DeleteApp()
    {
        if (SelectedApp is not { } item || !confirmDelete($"Xóa “{item.Name}” khỏi danh sách cấu hình?\nKhông gỡ ứng dụng đã cài trên máy. Thay đổi có hiệu lực sau khi Lưu.")) return;
        var index = EditableApps.IndexOf(item);
        EditableApps.Remove(item); SelectedApp = EditableApps.Count == 0 ? null : EditableApps[Math.Min(index, EditableApps.Count - 1)];
        SettingsMessage = "Đã xóa khỏi bản nháp ứng dụng. Bấm Lưu thay đổi để áp dụng.";
    }
    private void DeleteWindows()
    {
        if (SelectedWindows is not { } item || !confirmDelete($"Xóa thiết lập “{item.Name}” khỏi danh sách?\nKhông hoàn tác thay đổi đã áp dụng cho Windows. Thay đổi có hiệu lực sau khi Lưu.")) return;
        var index = EditableWindows.IndexOf(item);
        EditableWindows.Remove(item); SelectedWindows = EditableWindows.Count == 0 ? null : EditableWindows[Math.Min(index, EditableWindows.Count - 1)];
        SettingsMessage = "Đã xóa khỏi bản nháp thiết lập. Bấm Lưu thay đổi để áp dụng.";
    }
    private void Save()
    {
        try
        {
            if (SettingsTab == 0)
            {
                Catalog.Validate(EditableApps);
                if (settingsWritable) store.Save(EditableApps);
                catalog = JsonSerializer.Deserialize<List<AppDefinition>>(JsonSerializer.Serialize(EditableApps))!;
            }
            else
            {
                WindowsSettingsCatalog.Validate(EditableWindows);
                if (settingsWritable) store.SaveWindows(EditableWindows);
                windowsCatalog = JsonSerializer.Deserialize<List<WindowsSettingDefinition>>(JsonSerializer.Serialize(EditableWindows))!;
                // All saved definitions participate in the next explicitly confirmed run.
                RebuildWindows();
            }
            RebuildRows();
            var section = SettingsTab == 0 ? "ứng dụng" : "các thiết lập";
            SettingsMessage = settingsWritable ? $"Đã lưu {section} tại {store.DirectoryPath}" : $"Đã áp dụng {section} trong bản xem thử; không ghi cấu hình xuống máy.";
            RefreshSettingsState();
        }
        catch (Exception ex) { SettingsMessage = "Chưa lưu: " + ex.Message; }
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
#if NET48
        if (OptimizeService.IsAppxWorkerActive())
        {
            MessageBox.Show("Một tác vụ gỡ package quá hạn vẫn đang được Windows xử lý. Chưa thể bắt đầu cài đặt.", "MiniApps");
            return;
        }
#endif
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
#if NET48
        var smartSkipped = new HashSet<string>(); var unverified = new HashSet<string>();
#endif
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
#if NET48
                if (!isWindows && e.Status.StartsWith("Đã cài", StringComparison.Ordinal)) smartSkipped.Add(e.Id);
                if (!isWindows && e.Status.StartsWith("Không xác minh được", StringComparison.Ordinal)) unverified.Add(e.Id);
#endif
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
#if NET48
                : $"Hoàn tất · {finished.Count}/{total} tác vụ · bỏ qua {smartSkipped.Count} đã cài · chưa xác minh {unverified.Count} · {failed.Count} lỗi";
            runCompleted = !cancellation.IsCancellationRequested && failed.Count == 0;
#else
                : $"Hoàn tất · {finished.Count}/{total} tác vụ · {failed.Count} lỗi";
            runCompleted = !cancellation.IsCancellationRequested;
#endif
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
