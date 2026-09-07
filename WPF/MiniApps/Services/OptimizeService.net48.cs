using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniApps.Models;

namespace MiniApps.Services;

public enum OptimizeStage
{
    Ready,
    Downloading,
    Preparing,
    Applying,
    Completed,
    Error
}

public enum OptimizeTaskEvent { Queued, Start, Done, Skip, Error }
public sealed record OptimizeTaskUpdate(OptimizeTaskEvent Event, string Id, string Message);
public sealed record OptimizeProgress(OptimizeStage Stage, string Message, string LogDirectory = "", OptimizeTaskUpdate? Task = null);
public sealed record OptimizeResult(bool Succeeded, string Message, string LogDirectory);

public interface IOptimizeService
{
    Task<OptimizeResult> RunAsync(IProgress<OptimizeProgress> progress, CancellationToken cancellationToken);
}

#if NET48
public sealed class OptimizeService : IOptimizeService
{
    internal const string DeploymentMutexName = "Global\\MiniApps.Deployment";
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps", "OptimizeLogs");

    private readonly Func<string, string, IProgress<string>, Task<int>> runPowerShell;
    private readonly Func<bool> isAdministrator;
    private readonly Func<Mutex> createMutex;
    private readonly string scriptPath;
    private readonly string tempRoot;

    public OptimizeService(
        Func<string, string, IProgress<string>, Task<int>>? runPowerShell = null,
        Func<bool>? isAdministrator = null,
        Func<Mutex>? createMutex = null,
        string? scriptPath = null,
        string? tempRoot = null)
    {
        this.runPowerShell = runPowerShell ?? ((command, work, output) => DeploymentService.RunPowerShellAsync(command, work, output));
        this.isAdministrator = isAdministrator ?? IsCurrentProcessAdministrator;
        this.createMutex = createMutex ?? (() => new Mutex(false, DeploymentMutexName));
        this.scriptPath = scriptPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Optimize-Defaults.ps1");
        this.tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "MiniApps");
    }

    public async Task<OptimizeResult> RunAsync(IProgress<OptimizeProgress> progress, CancellationToken cancellationToken)
    {
        if (!isAdministrator())
            return new(false, "Hãy mở MiniApps với quyền Administrator để tối ưu Windows.", "");

        using var gate = createMutex();
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                return new(false, "Một lượt cài đặt hoặc tối ưu khác đang chạy. Hãy chờ hoàn tất.", "");

            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(scriptPath))
                return new(false, "Thiếu script Optimize của MiniApps.", "");

            // The bootstrap already nests MiniApps under its owned session directory. Keep this
            // leaf unique for direct launches too, but short enough for legacy MAX_PATH APIs.
            var work = Path.Combine(tempRoot, "o-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(work);
            var lastStage = OptimizeStage.Ready;
            var logDirectory = "";
            var taskStates = new Dictionary<string, OptimizeTaskEvent>(StringComparer.OrdinalIgnoreCase);
            var processOutput = new List<string>();
            var processOutputLock = new object();
            try
            {
                var callerContext = SynchronizationContext.Current;
                var output = new CallbackProgress<string>(line =>
                {
                    if (!TryParseProtocolLine(line, out var item))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lock (processOutputLock)
                            {
                                processOutput.Add(line.Trim());
                                if (processOutput.Count > 40) processOutput.RemoveAt(0);
                            }
                        }
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(item.LogDirectory)) logDirectory = item.LogDirectory;
                    // The script's Completed marker means its process reached the end. Only the
                    // service may publish Completed after validating every task result below.
                    if (item.Stage == OptimizeStage.Completed) return;
                    if (item.Stage != OptimizeStage.Ready) lastStage = item.Stage;
                    if (item.Task is { } task) taskStates[task.Id] = task.Event;
                    if (callerContext == null || ReferenceEquals(callerContext, SynchronizationContext.Current)) progress.Report(item);
                    else callerContext.Send(_ => progress.Report(item), null);
                });
                var escapedWork = EscapePowerShell(work);
                var escapedScript = EscapePowerShell(scriptPath);
                var command = $"Set-Location -LiteralPath '{escapedWork}'; & '{escapedScript}'";
                var code = await runPowerShell(command, work, output);
                cancellationToken.ThrowIfCancellationRequested();
                if (code != 0)
                    throw new InvalidOperationException($"Win11Debloat báo lỗi ở bước {StageLabel(lastStage)} (mã {code}).{PowerShellDetail(processOutput, processOutputLock)}");
                var failedTasks = taskStates.Where(item => item.Value == OptimizeTaskEvent.Error).Select(item => item.Key).ToArray();
                if (failedTasks.Length > 0)
                    throw new InvalidOperationException($"{failedTasks.Length} tác vụ báo lỗi: {string.Join(", ", failedTasks)}.");
                var terminalEvents = new[] { OptimizeTaskEvent.Done, OptimizeTaskEvent.Skip, OptimizeTaskEvent.Error };
                var incompleteTasks = OptimizeTaskCatalog.Defaults()
                    .Select(task => task.Id)
                    .Where(id => !taskStates.TryGetValue(id, out var state) || !terminalEvents.Contains(state))
                    .ToArray();
                if (incompleteTasks.Length > 0)
                    throw new InvalidOperationException($"{incompleteTasks.Length} tác vụ chưa có kết quả xác nhận: {string.Join(", ", incompleteTasks)}.");

                var message = "Đã áp dụng cấu hình Default. Hãy đăng xuất hoặc khởi động lại để hoàn tất.";
                progress.Report(new(OptimizeStage.Completed, message, logDirectory));
                return new(true, message, logDirectory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var detail = ex is InvalidOperationException && ex.Message.Contains("Chi tiết PowerShell:")
                    ? ""
                    : PowerShellDetail(processOutput, processOutputLock);
                var message = $"Không thể hoàn tất ở bước {StageLabel(lastStage)}: {ex.Message}{detail}";
                progress.Report(new(OptimizeStage.Error, message, logDirectory));
                return new(false, message, logDirectory);
            }
            finally
            {
                TryCleanWorkDirectory(work);
            }
        }
        finally
        {
            if (acquired) gate.ReleaseMutex();
        }
    }

    public static bool TryParseProtocolLine(string line, out OptimizeProgress progress)
    {
        const string stagePrefix = "MINIAPPS_STAGE:";
        const string logPrefix = "MINIAPPS_LOG:";
        const string taskPrefix = "MINIAPPS_TASK_JSON:";
        if (line.StartsWith(taskPrefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<TaskProtocolEnvelope>(line.Substring(taskPrefix.Length).Trim());
                if (envelope != null && !string.IsNullOrWhiteSpace(envelope.Id) && Enum.TryParse(envelope.Event, true, out OptimizeTaskEvent taskEvent) &&
                    Enum.IsDefined(typeof(OptimizeTaskEvent), taskEvent) && !int.TryParse(envelope.Event, out _))
                {
                    progress = new(OptimizeStage.Ready, "", "", new(taskEvent, envelope.Id, envelope.Message ?? ""));
                    return true;
                }
            }
            catch (JsonException) { }
        }
        if (line.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = line.Substring(stagePrefix.Length).Trim();
            if (Enum.TryParse(value, true, out OptimizeStage stage))
            {
                progress = new(stage, StageMessage(stage));
                return true;
            }
        }
        if (line.StartsWith(logPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = line.Substring(logPrefix.Length).Trim();
            progress = new(OptimizeStage.Ready, "", path);
            return true;
        }
        progress = new(OptimizeStage.Ready, "");
        return false;
    }

    private sealed class TaskProtocolEnvelope
    {
        [JsonPropertyName("event")] public string Event { get; set; } = "";
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    private static string StageMessage(OptimizeStage stage) => stage switch
    {
        OptimizeStage.Downloading => "Đang tải cấu hình Win11Debloat đã được ghim…",
        OptimizeStage.Preparing => "Đang chuẩn bị cấu hình Win11Debloat…",
        OptimizeStage.Applying => "Đang áp dụng cấu hình Default…",
        OptimizeStage.Completed => "Đã tối ưu Windows.",
        OptimizeStage.Error => "Không thể hoàn tất tối ưu.",
        _ => "Sẵn sàng tối ưu Windows."
    };

    private static string StageLabel(OptimizeStage stage) => stage switch
    {
        OptimizeStage.Downloading => "tải xuống",
        OptimizeStage.Preparing => "chuẩn bị",
        OptimizeStage.Applying => "áp dụng",
        _ => "chuẩn bị"
    };

    private static bool IsCurrentProcessAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    private static string PowerShellDetail(List<string> lines, object sync)
    {
        string detail;
        lock (sync) detail = string.Join(Environment.NewLine, lines);
        if (string.IsNullOrWhiteSpace(detail)) return "";
        if (detail.Length > 4000) detail = detail.Substring(detail.Length - 4000);
        return Environment.NewLine + "Chi tiết PowerShell: " + detail;
    }

    private static void TryCleanWorkDirectory(string work)
    {
        try
        {
            if (!Directory.Exists(work)) return;
            if (Directory.GetDirectories(work, "Backups", SearchOption.AllDirectories).Length == 0)
                Directory.Delete(work, true);
        }
        catch { }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

public sealed class OptimizePreviewService : IOptimizeService
{
    private readonly int delayMilliseconds;
    public OptimizePreviewService(int delayMilliseconds = 80) => this.delayMilliseconds = delayMilliseconds;

    public async Task<OptimizeResult> RunAsync(IProgress<OptimizeProgress> progress, CancellationToken cancellationToken)
    {
        var stages = new[] { OptimizeStage.Downloading, OptimizeStage.Preparing, OptimizeStage.Applying };
        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new OptimizeProgress(stage, stage switch
            {
                OptimizeStage.Downloading => "Đang mô phỏng tải gói cấu hình…",
                OptimizeStage.Preparing => "Đang mô phỏng chuẩn bị cấu hình…",
                _ => "Đang mô phỏng áp dụng toàn bộ cấu hình…"
            });
            progress.Report(item);
            await Task.Delay(delayMilliseconds, cancellationToken);
        }
        var tasks = OptimizeTaskCatalog.Defaults();
        foreach (var task in tasks)
            progress.Report(new(OptimizeStage.Ready, "", "", new(OptimizeTaskEvent.Queued, task.Id, "Đang chờ")));
        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new(OptimizeStage.Applying, $"Đang mô phỏng {task.Name}…", "", new(OptimizeTaskEvent.Start, task.Id, "Đang chạy · mô phỏng")));
            await Task.Delay(delayMilliseconds, cancellationToken);
            progress.Report(new(OptimizeStage.Applying, $"Đã mô phỏng {task.Name}.", "", new(OptimizeTaskEvent.Done, task.Id, "Hoàn tất · mô phỏng")));
        }
        var message = "Đã xem trước toàn bộ quy trình. Không có thay đổi nào được áp dụng.";
        progress.Report(new(OptimizeStage.Completed, message));
        return new(true, message, "");
    }
}
#endif
