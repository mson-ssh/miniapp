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

public sealed class RelayCommand<T>(Action<T> action, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => parameter is T value && (canExecute?.Invoke(value) ?? true);
    public void Execute(object? parameter) { if (parameter is T value && CanExecute(value)) action(value); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record SuiteChoice(string Value, string Label);
public sealed class ExtensionItem(string id, string name, string description, string actionText, string confirmText, bool interactive = false) : Observable
{
    // Interactive tools open in their own window and are driven there; they are not run hidden.
    public bool Interactive { get; } = interactive;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string ActionText { get; } = actionText;
    public string ConfirmText { get; } = confirmText;
    private string status = "Sẵn sàng";
    public string Status { get => status; set => Set(ref status, value); }
    private string detail = "";
    // The latest output line while running, or why it failed.
    public string Detail { get => detail; set { if (Set(ref detail, value)) Raise(nameof(HasDetail)); } }
    public bool HasDetail => Detail.Length > 0;
    private bool isRunning;
    public bool IsRunning { get => isRunning; set => Set(ref isRunning, value); }
    private string logPath = "";
    public string LogPath { get => logPath; set { if (Set(ref logPath, value)) Raise(nameof(HasLog)); } }
    public bool HasLog => LogPath.Length > 0;
}

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
    internal bool IsPreview => preview;
    private CancellationTokenSource? cancellation;
    public ObservableCollection<AppRow> Apps { get; } = [];
    // Every catalog app, Office and WPS included, for installing one app on its own.
    public ObservableCollection<AppRow> ReadyApps { get; } = [];
    public ObservableCollection<AppRow> SystemTasks { get; } = [];
    public ObservableCollection<AppRow> WindowsTaskDetails { get; } = [];
    public ObservableCollection<AppRow> ProgressRows { get; } = [];
    public ObservableCollection<WindowsOption> WindowsOptions { get; } = [];
    // Office suite installed with the main Cài đặt button; "" installs none.
    public IReadOnlyList<SuiteChoice> OfficeSuites { get; } =
    [
        new("Office", "Microsoft Office"),
        new("WPS", "WPS"),
        new("OnlyOffice", "OnlyOffice"),
        new("LibreOffice", "Libre Office"),
        new("", "Chọn Office")
    ];
    private string selectedSuite = "";
    public string SelectedSuite
    {
        get => selectedSuite;
        set { if (CanChooseSuite && Set(ref selectedSuite, value ?? "")) RebuildRows(); }
    }
    public bool CanChooseSuite => IsIdle && !HasStarted;
    // Any suite other than Microsoft Office takes its place, so the Office already installed is removed.
    public bool RemovesOffice => SelectedSuite is "WPS" or "OnlyOffice" or "LibreOffice";
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
            if (value is < 0 or > 2) return;
            if (Set(ref page, value))
            {
                Raise(nameof(IsInstall)); Raise(nameof(IsExtend));
            }
        }
    }
    public bool IsInstall => Page == 0;
    public bool IsExtend => Page == 2;
    // Optional add-ons on the EXTEND page, each run on its own after the technician confirms.
    public IReadOnlyList<ExtensionItem> Extensions { get; } =
    [
        new("cpp", "Môi trường C++",
            "VS Code, MSYS2, bộ biên dịch MinGW-w64 (gcc, g++, gdb) và extension C/C++; thêm bộ biên dịch vào PATH của máy.",
            "Cài đặt",
            "Cài môi trường C/C++?\n\n" +
            "• VS Code, MSYS2 và bộ biên dịch MinGW-w64 (UCRT64).\n" +
            "• Thêm C:\\msys64\\ucrt64\\bin vào PATH của máy.\n" +
            "• Cài extension C/C++ cho VS Code.\n\n" +
            "Tải khoảng 2 GB, có thể mất 10–30 phút. Phần đã có trên máy sẽ được bỏ qua."),
        new("sharelan", "Share LAN",
            "Chia sẻ ổ đĩa/thư mục qua mạng LAN không cần mật khẩu, kết nối tới máy đang chia sẻ, quản lý hoặc chẩn đoán. Mở trong cửa sổ PowerShell riêng.",
            "Mở", "", interactive: true),
        new("clean", "CLEAN",
            "Dọn file tạm của Windows, danh sách Recent, lịch sử và cache trình duyệt của tài khoản Windows hiện tại.",
            "Dọn dẹp",
            "Chạy CLEAN cho tài khoản " + Environment.UserName + "?\n\n" +
            "• Xóa file trong %TEMP% và C:\\Windows\\Temp.\n" +
            "• Xóa Recent của Windows: file gần đây, jump list, lịch sử Run, thanh địa chỉ và tìm kiếm Explorer, hộp Mở/Lưu. Giữ thư mục ghim Quick access.\n" +
            "• Xóa lịch sử/cache của các profile trình duyệt được hỗ trợ; giữ cookie/đăng nhập, mật khẩu, dấu trang và file đã tải.\n" +
            "• Trình duyệt đang mở (kể cả chạy nền) sẽ bị ĐÓNG để dọn. Hãy lưu nội dung đang nhập trên web trước.\n" +
            "• Không chạy khi phần mềm khác đang cài đặt.\n" +
            "• File đang dùng, liên kết thư mục và thư mục backup được giữ lại.\n\n" +
            "Dữ liệu đã xóa không vào Thùng rác. Chỉ dọn dữ liệu cục bộ; đồng bộ có thể đưa lịch sử trở lại.\n" +
            "Hỗ trợ profile chuẩn Chrome, Edge, Cốc Cốc, Brave, Opera, Vivaldi, Chromium và Firefox; không tự quét profile portable/vị trí tùy chỉnh ngoài tài khoản này."),
    ];
    public RelayCommand<ExtensionItem> RunExtensionCommand { get; }
    public RelayCommand<ExtensionItem> OpenExtensionLogCommand { get; }
    private readonly Func<string, bool> confirm;
    private readonly Func<string, IProgress<string>, Task<ExtensionRunResult>> runExtension;
    private readonly Action<string> launchTool;
    public string SingleInstallIcon => "M 8,0 L 12,0 L 12,8 L 16,8 L 10,14 L 4,8 L 8,8 Z M 0,13 L 3,13 L 3,17 L 17,17 L 17,13 L 20,13 L 20,20 L 0,20 Z";
    public double SingleInstallIconSize => 18;
    public string SingleInstallIconColor => "#111111";
    public string SingleInstallHint => "Chọn bộ văn phòng ở danh sách cạnh nút Cài đặt (Chọn Office: không cài bộ nào); chọn WPS, OnlyOffice hoặc Libre Office sẽ gỡ Microsoft Office đang có trên máy. Nút tải xuống ở từng ứng dụng để cài riêng ứng dụng đó.";
    private bool busy;
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) { Raise(nameof(IsIdle)); Raise(nameof(ShowInstallCancel)); Raise(nameof(CanChooseSuite)); Refresh(); } } }
    public bool IsIdle => !IsBusy;
    private bool installRunning;
    public bool IsInstallRunning { get => installRunning; private set { if (Set(ref installRunning, value)) { Raise(nameof(ShowInstallCancel)); Raise(nameof(InstallButtonText)); Raise(nameof(IsCancelButton)); InstallCommand?.Refresh(); } } }
    public bool IsCancelButton => IsInstallRunning;
    private bool cancellingInstall;
    public bool IsCancellingInstall { get => cancellingInstall; private set { if (Set(ref cancellingInstall, value)) { Raise(nameof(InstallButtonText)); InstallCommand?.Refresh(); CancelCommand?.Refresh(); } } }
    public bool ShowInstallCancel => false;
    private const string CancelledSummary = "Đã hủy · đã dừng nhóm tiến trình của lượt cài. Thay đổi đã áp dụng không được hoàn tác; dịch vụ Windows có thể còn xử lý.";
    private void CancelInstall()
    {
        var source = cancellation;
        if (source == null || IsCancellingInstall || !IsInstallRunning) return;
        if (!confirm("HỦY các tác vụ đang chạy?\n\nSẽ dừng cưỡng bức cây tiến trình của lượt cài, kể cả bộ cài và Windows Setting.\nPhần mềm có thể cài dở; thay đổi Windows/ổ đĩa đã áp dụng không được hoàn tác. Dịch vụ hệ thống xử lý thay có thể vẫn tiếp tục.\n\nTiếp tục hủy?")) return;
        // A modal confirmation pumps the dispatcher: the run may finish while it is open.
        if (cancellation != source || !IsInstallRunning) return;
        IsCancellingInstall = true;
        Summary = "Đang hủy · dừng cây tiến trình của lượt cài… Nếu bị treo, có thể đóng cửa sổ để thoát.";
        // Cancel callbacks terminate jobs and close download streams; keep them off the UI thread.
        // The run may still finish and dispose the source first.
        _ = Task.Run(() => { try { source.Cancel(); } catch (ObjectDisposedException) { } });
    }
    // Escape hatch requested by the user: a process that ignores termination must not trap MiniApps.
    // Offered only after HỦY was confirmed. Windows releases the install mutex when MiniApps exits;
    // the work folder is removed by WorkFolderCleaner on the next start.
    public bool ConfirmExitWhileCancelling() => IsCancellingInstall && confirm(
        "MiniApps vẫn đang chờ tiến trình của lượt cài dừng hẳn.\n\n" +
        "Thoát MiniApps ngay?\n" +
        "• Tiến trình chưa dừng được có thể vẫn chạy nền; kiểm tra trong Task Manager nếu cần.\n" +
        "• Thư mục tạm của lượt cài sẽ được dọn ở lần mở MiniApps sau.");
    private bool hasStarted;
    public bool HasStarted { get => hasStarted; private set { Set(ref hasStarted, value); Raise(nameof(IsReady)); Raise(nameof(CanChooseSuite)); } }
    public bool IsReady => !HasStarted;
    private bool installFinished;
    public bool InstallFinished { get => installFinished; private set { if (Set(ref installFinished, value)) { Raise(nameof(InstallButtonText)); InstallCommand?.Refresh(); } } }
    public string InstallButtonText =>
        IsInstallRunning ? (IsCancellingInstall ? "ĐANG HỦY…" : "HỦY") :
        InstallFinished ? "Đã hoàn tất" : "Cài đặt";
    private bool isWindowsDetailsVisible;
    public bool IsWindowsDetailsVisible { get => isWindowsDetailsVisible; private set => Set(ref isWindowsDetailsVisible, value); }
    private string summary = "Sẵn sàng cài toàn bộ danh sách đã cấu hình.";
    public string Summary { get => summary; private set => Set(ref summary, value); }
    // Installers and Debloat print thousands of lines; appending to one growing string copied it every time.
    private readonly System.Text.StringBuilder details = new();
    public string Details => details.ToString();
    private double overall;
    public double Overall { get => overall; private set => Set(ref overall, value); }
    // Debloat is counted with the apps, matching the card it gets in the progress list.
    public string SelectionText
    {
        get
        {
            var appLike = WindowsOptions.Count(option => IsAppLikeTask(option.Definition));
            return $"{Apps.Count + appLike} ứng dụng · {WindowsOptions.Count - appLike} thiết lập Windows";
        }
    }
    public RelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand<AppRow> InstallOneCommand { get; }
    public RelayCommand ToggleWindowsDetailsCommand { get; }

    public MainViewModel(bool preview = false,
        string? settingsDirectory = null, bool requireSettings = false,
        Func<string, bool>? confirm = null, Func<string, IProgress<string>, Task<ExtensionRunResult>>? runExtension = null,
        Action<string>? launchTool = null)
    {
        this.preview = preview;
        this.confirm = confirm ?? (text => MessageBox.Show(text, "MiniApps", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        this.runExtension = runExtension ?? ((id, progress) => new ExtensionService().RunAsync(id, progress));
        this.launchTool = launchTool ?? ExtensionService.LaunchTool;
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
        InstallCommand = new RelayCommand(async () => { if (IsInstallRunning) CancelInstall(); else await InstallAsync(); },
            () => IsInstallRunning ? !IsCancellingInstall && cancellation != null : !IsBusy && !InstallFinished && (Apps.Count > 0 || WindowsOptions.Count > 0));
        CancelCommand = new RelayCommand(CancelInstall, () => IsInstallRunning && cancellation != null && !IsCancellingInstall);
        InstallOneCommand = new RelayCommand<AppRow>(async row => await InstallOneAsync(row), _ => !IsBusy);
        // An interactive tool runs in its own window, so it stays available while an install runs.
        RunExtensionCommand = new RelayCommand<ExtensionItem>(async item => await RunExtensionAsync(item), item => item.Interactive || !IsBusy);
        OpenExtensionLogCommand = new RelayCommand<ExtensionItem>(OpenExtensionLog, item => item.HasLog);
        ToggleWindowsDetailsCommand = new RelayCommand(() => IsWindowsDetailsVisible = !IsWindowsDetailsVisible, () => WindowsTaskDetails.Count > 0);
        foreach (var app in catalog) ReadyApps.Add(new AppRow(app));
        RebuildWindows();
        // Debloat is listed with the apps too, so it can also be run on its own.
        foreach (var option in WindowsOptions.Where(option => IsAppLikeTask(option.Definition)))
            ReadyApps.Add(new AppRow(new AppDefinition { Id = option.Definition.TaskId, Name = option.Name }));
        RebuildRows();
    }
    private void Refresh() { Raise(nameof(SelectionText)); InstallCommand?.Refresh(); CancelCommand?.Refresh(); InstallOneCommand?.Refresh(); RunExtensionCommand?.Refresh(); }
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
    private static bool IsAppLikeTask(WindowsSettingDefinition definition) => definition.Action == "Win11Debloat";
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
        foreach (var app in catalog.Where(a => a.Suite.Length == 0 || a.Suite == SelectedSuite))
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
    private void Log(string line) => details.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").Append(line).Append('\n');
    private bool EnsureAdministrator()
    {
        if (preview) return true;
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return true;
        MessageBox.Show("Hãy chạy bằng bootstrap hoặc mở MiniApps với quyền Administrator. Chưa có tác vụ nào được chạy.", "MiniApps", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
    private static bool TryAcquire(Mutex deploymentLock)
    {
        bool acquired;
        try { acquired = deploymentLock.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) MessageBox.Show("Một cửa sổ MiniApps khác đang cài đặt. Hãy chờ lượt đó kết thúc.", "MiniApps");
        return acquired;
    }
    // Runs the apps and settings in a temporary work folder (or simulates them in preview), then removes the folder.
    private async Task RunDeploymentAsync(List<AppDefinition> apps, List<WindowsSettingDefinition> options, IProgress<DeploymentEvent> events, CancellationToken token)
    {
        var workDir = Path.Combine(WorkFolderCleaner.DefaultRoot, "work-" + Guid.NewGuid().ToString("N"));
        string? marker = null;
        try
        {
            marker = CreateSessionMarker();
            if (preview)
            {
                var previewApps = apps.Select(async app =>
                {
                    token.ThrowIfCancellationRequested();
                    events.Report(new(app.Id, "Đang tải · mô phỏng", 45));
                    await Task.Delay(180, token);
                    events.Report(new(app.Id, "Đang cài · mô phỏng", 100));
                    await Task.Delay(700, token);
                    events.Report(new(app.Id, "Hoàn tất · mô phỏng", 100, true));
                });
                var previewSettings = options.Select(async option =>
                {
                    token.ThrowIfCancellationRequested();
                    events.Report(new(option.TaskId, "Đang áp dụng", 0));
                    await Task.Delay(500, token);
                    events.Report(new(option.TaskId, "Hoàn tất · mô phỏng", 100, true));
                });
                await Task.WhenAll(previewApps.Concat(previewSettings));
            }
            else
            {
                // Created here, so progress still reaches the window; the work itself (downloads,
                // SHA-256 of large installers, detection) runs off the UI thread and cannot stall it.
                var log = new Progress<string>(Log);
                await Task.Run(() => new DeploymentService().RunAsync(apps, options, workDir, events, log, token));
            }
            // Drain progress callbacks before the caller composes its final summary.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
        finally
        {
            // Cancellation also needs queued progress drained before the final summary/reset.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); }
            catch (Exception ex) { Log("Chưa dọn hết bộ cài tạm: " + ex.Message); }
            RemoveSessionMarker(marker);
        }
    }
    // Under bootstrap, tells a later run not to clean this session while installers may still run.
    private string? CreateSessionMarker()
    {
        var session = Environment.GetEnvironmentVariable("MINIAPPS_SESSION");
        if (preview || string.IsNullOrEmpty(session)) return null;
        var marker = Path.Combine(session, "installing");
        File.WriteAllText(marker, "Do not clean until process tree exits.");
        return marker;
    }
    private static void RemoveSessionMarker(string? marker)
    {
        if (marker != null) { try { File.Delete(marker); } catch (IOException) { } }
    }
    private async Task InstallAsync()
    {
        var options = WindowsOptions.Select(o => o.Definition).ToList();
        if (RemovesOffice && !options.Any(o => o.Id.Equals("RemoveOffice", StringComparison.OrdinalIgnoreCase)))
            options.Insert(0, WindowsSettingsCatalog.RemoveOffice());
        if (!EnsureAdministrator()) return;
        RebuildRows();
        var selected = Apps.Select(a => a.Definition).ToList();
        using var deploymentLock = new Mutex(false, DeploymentService.DeploymentLockName);
        if (!TryAcquire(deploymentLock)) return;
        IsBusy = true; IsInstallRunning = true; details.Clear(); Overall = 0;
        HasStarted = true;
        // Debloat runs for minutes and changes the machine the most, so it gets its own card like an app.
        foreach (var option in options.Where(IsAppLikeTask))
            ProgressRows.Add(new AppRow(new AppDefinition { Id = option.TaskId, Name = option.Name }) { Status = "Chờ chạy" });
        var settings = options.Where(option => !IsAppLikeTask(option)).ToList();
        if (settings.Count > 0)
        {
            var windowsRow = new AppRow(new AppDefinition { Id = "windows:summary", Name = "Windows Setting" }) { Status = $"Chờ áp dụng · 0/{settings.Count}" };
            SystemTasks.Add(windowsRow);
            ProgressRows.Add(windowsRow);
            foreach (var option in settings)
                WindowsTaskDetails.Add(new AppRow(new AppDefinition { Id = option.TaskId, Name = option.Name }) { Status = "Chờ áp dụng" });
            ToggleWindowsDetailsCommand.Refresh();
        }
        foreach (var row in Apps) { row.Status = "Chờ tải"; row.Progress = 0; }
        cancellation = new CancellationTokenSource();
        IsCancellingInstall = false; InstallCommand.Refresh(); CancelCommand.Refresh();
        var total = selected.Count + options.Count;
        var finished = new HashSet<string>(); var failed = new HashSet<string>();
        var smartSkipped = new HashSet<string>(); var unverified = new HashSet<string>();
        var windowsIds = settings.Select(o => o.TaskId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var windowsNames = settings.ToDictionary(o => o.TaskId, o => o.Name, StringComparer.OrdinalIgnoreCase);
        var windowsProgress = new WindowsProgressTracker(settings.Count);
        var events = new Progress<DeploymentEvent>(e =>
        {
            var isWindows = windowsIds.Contains(e.Id);
            var row = isWindows ? SystemTasks.FirstOrDefault() : ProgressRows.FirstOrDefault(a => a.Definition.Id == e.Id);
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
            if (!IsCancellingInstall)
                Summary = $"Đã xử lý {finished.Count}/{total} · {failed.Count} lỗi";
        });
        var runCompleted = false;
        try
        {
            Summary = preview ? "Đang mô phỏng · không tải hoặc chạy bộ cài" : "Đang chuẩn bị cài đặt…";
            await RunDeploymentAsync(selected, options, events, cancellation.Token);
            Summary = cancellation.IsCancellationRequested ?
                CancelledSummary
                : $"Hoàn tất · {finished.Count}/{total} tác vụ · bỏ qua {smartSkipped.Count} đã cài · chưa xác minh {unverified.Count} · {failed.Count} lỗi";
            runCompleted = !cancellation.IsCancellationRequested && failed.Count == 0;
        }
        catch (OperationCanceledException) {
            Summary = CancelledSummary;
        }
        catch (Exception ex) { Log(ex.ToString()); Summary = "Có lỗi trong quá trình cài đặt."; }
        finally
        {
            if (cancellation.IsCancellationRequested)
            {
                foreach (var row in ProgressRows.Where(row => !row.IsWindowsSummary && !finished.Contains(row.Definition.Id))) row.Status = "Đã hủy";
                foreach (var row in WindowsTaskDetails.Where(row => !finished.Contains(row.Definition.Id)))
                {
                    row.Status = "Đã hủy";
                    var summaryRow = SystemTasks.FirstOrDefault();
                    if (summaryRow != null) { summaryRow.Status = windowsProgress.Update(row.Definition.Id, "Đã hủy", true, false); summaryRow.Progress = windowsProgress.Progress; }
                }
            }
            IsCancellingInstall = false;
            cancellation.Dispose(); cancellation = null; InstallFinished = runCompleted; IsInstallRunning = false; IsBusy = false;
            deploymentLock.ReleaseMutex();
        }
    }
    // Installs one app from the ready list, or runs Debloat alone: no office suite and no other Windows settings.
    private async Task InstallOneAsync(AppRow row)
    {
        var setting = WindowsOptions.Select(option => option.Definition).FirstOrDefault(definition => definition.TaskId == row.Definition.Id);
        if (IsBusy || !EnsureAdministrator()) return;
        using var deploymentLock = new Mutex(false, DeploymentService.DeploymentLockName);
        if (!TryAcquire(deploymentLock)) return;
        IsBusy = true; IsInstallRunning = true;
        row.Status = setting == null ? "Chờ tải" : "Chờ chạy"; row.Progress = 0;
        cancellation = new CancellationTokenSource();
        IsCancellingInstall = false; InstallCommand.Refresh(); CancelCommand.Refresh();
        var events = new Progress<DeploymentEvent>(e =>
        {
            if (e.Id != row.Definition.Id) return;
            row.Status = e.Status; row.Progress = e.Progress;
            if (e.Finished) Log($"{row.Name}: {e.Status}");
        });
        try
        {
            Summary = preview ? $"Đang mô phỏng {row.Name}…" : setting == null ? $"Đang cài {row.Name}…" : $"Đang chạy {row.Name}…";
            await RunDeploymentAsync(setting == null ? [row.Definition] : [], setting == null ? [] : [setting], events, cancellation.Token);
            Summary = cancellation.IsCancellationRequested ?
                CancelledSummary
                : $"{row.Name}: {row.Status}";
        }
        catch (OperationCanceledException) { row.Status = "Đã hủy"; Summary = "Đã hủy lượt chạy."; }
        catch (Exception ex) { Log(ex.ToString()); row.Status = "Thất bại"; Summary = $"Có lỗi khi cài {row.Name}."; }
        finally
        {
            if (cancellation.IsCancellationRequested) { row.Status = "Đã hủy"; Summary = CancelledSummary; }
            IsCancellingInstall = false;
            cancellation.Dispose(); cancellation = null; IsInstallRunning = false; IsBusy = false;
            deploymentLock.ReleaseMutex();
        }
    }
    // Runs one EXTEND add-on after confirmation. It shares the install lock, so it never
    // overlaps an install in this or another MiniApps window.
    private async Task RunExtensionAsync(ExtensionItem item)
    {
        if (item.Interactive) { LaunchTool(item); return; }
        if (IsBusy || !confirm(item.ConfirmText) || !EnsureAdministrator()) return;
        using var deploymentLock = new Mutex(false, DeploymentService.DeploymentLockName);
        if (!TryAcquire(deploymentLock)) return;
        IsBusy = true;
        item.IsRunning = true; item.Status = "Đang chạy…"; item.Detail = ""; item.LogPath = "";
        // Output lines arrive on background threads; WPF marshals these simple property changes.
        // Only lines starting at column 0 are progress meant for the card; indented lines are
        // raw tool output (winget, pacman, banners) and stay in the log.
        var progress = new LineProgress(line => { if (IsProgressLine(line)) item.Detail = line.Trim(); });
        string? marker = null;
        try
        {
            marker = CreateSessionMarker();
            var result = preview ? await SimulateExtensionAsync(progress) : await Task.Run(() => runExtension(item.Id, progress));
            item.LogPath = result.LogPath;
            item.Status = result.ExitCode == 0 ? "Hoàn tất" :
                item.Id == "clean" && result.ExitCode == 2 ? "Chưa dọn hết · xem nhật ký" :
                $"Thất bại · mã {result.ExitCode}";
        }
        catch (Exception ex) { item.Status = "Thất bại"; item.Detail = ex.Message; }
        finally
        {
            RemoveSessionMarker(marker);
            item.IsRunning = false; IsBusy = false;
            OpenExtensionLogCommand.Refresh();
            deploymentLock.ReleaseMutex();
        }
    }
    private void LaunchTool(ExtensionItem item)
    {
        if (preview) { item.Status = "Mô phỏng"; item.Detail = "Sẽ mở công cụ trong cửa sổ PowerShell riêng."; return; }
        try
        {
            launchTool(item.Id);
            item.Status = "Đã mở";
            item.Detail = "Công cụ chạy trong cửa sổ PowerShell riêng; làm theo menu trong cửa sổ đó.";
        }
        catch (Exception ex) { item.Status = "Không mở được"; item.Detail = ex.Message; }
    }
    internal static bool IsProgressLine(string? line) =>
        !string.IsNullOrWhiteSpace(line) && !char.IsWhiteSpace(line![0]);
    private static async Task<ExtensionRunResult> SimulateExtensionAsync(IProgress<string> progress)
    {
        foreach (var line in new[] { "Kiểm tra máy · mô phỏng", "Đang tải · mô phỏng", "Đang áp dụng · mô phỏng" })
        {
            progress.Report(line);
            await Task.Delay(300);
        }
        return new(0, "");
    }
    private void OpenExtensionLog(ExtensionItem item)
    {
        if (!item.HasLog) return;
        try { Process.Start(new ProcessStartInfo(item.LogPath) { UseShellExecute = true }); }
        catch (Exception ex) { item.Detail = "Không mở được nhật ký: " + ex.Message; }
    }
    private sealed class LineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
