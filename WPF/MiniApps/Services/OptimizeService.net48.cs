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
#if NET48
    Overdue,
#endif
    Completed,
    Error
}

public enum OptimizeTaskEvent { Queued, Start, Done, Skip, Error, Overdue }
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
    internal const string AppxWorkerMutexName = "Global\\MiniApps.DebloatAppxWorker";
    internal const string EngineCommit = "6012b02ea282f23ea943946206762fd430025c6f";
    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps", "OptimizeLogs");

    private readonly Func<string, string, IProgress<string>, Task<int>> runPowerShell;
    private readonly Func<bool> isAdministrator;
    private readonly Func<Mutex> createMutex;
    private readonly Func<Mutex> createWorkerMutex;
    private readonly string scriptPath;
    private readonly string tempRoot;

    public OptimizeService(
        Func<string, string, IProgress<string>, Task<int>>? runPowerShell = null,
        Func<bool>? isAdministrator = null,
        Func<Mutex>? createMutex = null,
        string? scriptPath = null,
        string? tempRoot = null,
        Func<Mutex>? createWorkerMutex = null)
    {
        this.runPowerShell = runPowerShell ?? ((command, work, output) => DeploymentService.RunPowerShellAsync(
            command, work, output,
            "Optimize đã chạy hơn 30 phút. MiniApps vẫn đang nhận diện tiến trình và tiếp tục chờ; xem runtime-diagnostics.json trong thư mục nhật ký."));
        this.isAdministrator = isAdministrator ?? IsCurrentProcessAdministrator;
        this.createMutex = createMutex ?? (() => new Mutex(false, DeploymentMutexName));
        this.createWorkerMutex = createWorkerMutex ?? (() => new Mutex(false, AppxWorkerMutexName));
        this.scriptPath = scriptPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Optimize-Defaults.ps1");
        this.tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "MiniApps");
    }

    public async Task<OptimizeResult> RunAsync(IProgress<OptimizeProgress> progress, CancellationToken cancellationToken)
    {
        if (!isAdministrator())
            return new(false, "Hãy mở MiniApps với quyền Administrator để tối ưu Windows.", "");
        if (IsMutexHeld(createWorkerMutex))
            return new(false, "Một tác vụ gỡ package quá hạn vẫn đang được Windows xử lý. Hãy chờ worker kết thúc trước khi chạy lại.", "");

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
            var stateLock = new object();
            var diagnosticsWriteLock = new object();
            var startedAt = DateTimeOffset.UtcNow;
            DateTimeOffset? endedAt = null;
            DateTimeOffset? lastOutputAt = null;
            DateTimeOffset? lastProtocolAt = null;
            int? runnerPid = null;
            int? enginePid = null;
            var enginePids = new List<int>();
            int? appxWorkerPid = null;
            int? exitCode = null;
            var lifecycle = "Running";
            var failureText = "";
            try
            {
                var callerContext = SynchronizationContext.Current;
                var output = new CallbackProgress<string>(line =>
                {
                    lock (stateLock) lastOutputAt = DateTimeOffset.UtcNow;
                    if (TryParseRuntimeMetadata(line, out var isRunner, out var pid))
                    {
                        lock (stateLock)
                        {
                            if (isRunner) runnerPid = pid;
                            else
                            {
                                enginePid = pid;
                                if (!enginePids.Contains(pid)) enginePids.Add(pid);
                            }
                        }
                        TryWriteDiagnostics();
                        return;
                    }
                    if (TryParseAppxWorkerMetadata(line, out var workerPid))
                    {
                        lock (stateLock) appxWorkerPid = workerPid;
                        TryWriteDiagnostics();
                        return;
                    }
                    if (!TryParseProtocolLine(line, out var item))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lock (stateLock)
                            {
                                processOutput.Add(line.Trim());
                                if (processOutput.Count > 40) processOutput.RemoveAt(0);
                            }
                        }
                        TryWriteDiagnostics();
                        return;
                    }
                    lock (stateLock)
                    {
                        lastProtocolAt = DateTimeOffset.UtcNow;
                        if (!string.IsNullOrWhiteSpace(item.LogDirectory)) logDirectory = item.LogDirectory;
                    }
                    // The script's Completed marker means its process reached the end. Only the
                    // service may publish Completed after validating every task result below.
                    if (item.Stage == OptimizeStage.Completed) { TryWriteDiagnostics(); return; }
                    lock (stateLock)
                    {
                        if (item.Stage != OptimizeStage.Ready) lastStage = item.Stage;
                        if (item.Task is { } task) taskStates[task.Id] = task.Event;
                    }
                    if (callerContext == null || ReferenceEquals(callerContext, SynchronizationContext.Current)) progress.Report(item);
                    else callerContext.Send(_ => progress.Report(item), null);
                    TryWriteDiagnostics();
                });
                var escapedWork = EscapePowerShell(work);
                var escapedScript = EscapePowerShell(scriptPath);
                var command = $"Set-Location -LiteralPath '{escapedWork}'; & '{escapedScript}'";
                var code = await runPowerShell(command, work, output);
                lock (stateLock) exitCode = code;
                cancellationToken.ThrowIfCancellationRequested();
                if (code != 0)
                    throw new InvalidOperationException($"Win11Debloat báo lỗi ở bước {StageLabel(lastStage)} (mã {code}).{PowerShellDetail(processOutput, stateLock)}");
                string[] failedTasks;
                lock (stateLock) failedTasks = taskStates.Where(item => item.Value == OptimizeTaskEvent.Error).Select(item => item.Key).ToArray();
                if (failedTasks.Length > 0)
                    throw new InvalidOperationException($"{failedTasks.Length} tác vụ báo lỗi: {string.Join(", ", failedTasks)}.");
                var terminalEvents = new[] { OptimizeTaskEvent.Done, OptimizeTaskEvent.Skip, OptimizeTaskEvent.Error };
                string[] incompleteTasks;
                lock (stateLock) incompleteTasks = OptimizeTaskCatalog.Defaults()
                        .Select(task => task.Id)
                        .Where(id => !taskStates.TryGetValue(id, out var state) || !terminalEvents.Contains(state))
                        .ToArray();
                if (incompleteTasks.Length > 0)
                    throw new InvalidOperationException($"{incompleteTasks.Length} tác vụ chưa có kết quả xác nhận: {string.Join(", ", incompleteTasks)}.");

                lock (stateLock) { lifecycle = "Completed"; endedAt = DateTimeOffset.UtcNow; }
                TryWriteDiagnostics();
                var message = "Đã áp dụng cấu hình Default. " + TaskCountSummary(taskStates, stateLock) + " Hãy đăng xuất hoặc khởi động lại để hoàn tất.";
                progress.Report(new(OptimizeStage.Completed, message, logDirectory));
                return new(true, message, logDirectory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var detail = ex is InvalidOperationException && ex.Message.Contains("Chi tiết PowerShell:")
                    ? ""
                    : PowerShellDetail(processOutput, stateLock);
                bool overdue;
                lock (stateLock)
                {
                    overdue = taskStates.Values.Contains(OptimizeTaskEvent.Overdue);
                    lifecycle = overdue ? "Overdue" : "Failed";
                    endedAt = DateTimeOffset.UtcNow;
                    failureText = ex.ToString();
                }
                TryWriteDiagnostics();
                var message = $"Không thể hoàn tất ở bước {StageLabel(lastStage)}: {ex.Message}{detail} {TaskCountSummary(taskStates, stateLock)}";
                progress.Report(new(overdue ? OptimizeStage.Overdue : OptimizeStage.Error, message, logDirectory));
                return new(false, message, logDirectory);
            }
            finally
            {
                TryCleanWorkDirectory(work);
            }

            void TryWriteDiagnostics()
            {
                try
                {
                    OptimizeRunDiagnostics snapshot;
                    string destination;
                    lock (stateLock)
                    {
                        destination = logDirectory;
                        var expected = OptimizeTaskCatalog.Defaults().Select(task => task.Id).ToArray();
                        snapshot = new OptimizeRunDiagnostics
                        {
                            Lifecycle = lifecycle,
                            EngineCommit = EngineCommit,
                            RunnerPid = runnerPid,
                            EnginePid = enginePid,
                            EnginePids = enginePids.ToArray(),
                            AppxWorkerPid = appxWorkerPid,
                            StartedAtUtc = startedAt,
                            EndedAtUtc = endedAt,
                            LastOutputAtUtc = lastOutputAt,
                            LastProtocolAtUtc = lastProtocolAt,
                            Stage = lastStage.ToString(),
                            ExitCode = exitCode,
                            Succeeded = expected.Count(id => taskStates.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Done),
                            Skipped = expected.Count(id => taskStates.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Skip),
                            Failed = expected.Count(id => taskStates.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Error),
                            Overdue = expected.Count(id => taskStates.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Overdue),
                            NotRun = expected.Count(id => !taskStates.TryGetValue(id, out var value) || value is OptimizeTaskEvent.Queued or OptimizeTaskEvent.Start),
                            Failure = failureText
                        };
                    }
                    if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination)) return;
                    lock (diagnosticsWriteLock)
                        File.WriteAllText(Path.Combine(destination, "runtime-diagnostics.json"),
                            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }
        finally
        {
            if (acquired) gate.ReleaseMutex();
        }
    }

    internal static bool TryParseRuntimeMetadata(string line, out bool isRunner, out int pid)
    {
        const string runnerPrefix = "MINIAPPS_RUNNER_PID:";
        const string enginePrefix = "MINIAPPS_ENGINE_PID:";
        isRunner = line.StartsWith(runnerPrefix, StringComparison.OrdinalIgnoreCase);
        var prefix = isRunner ? runnerPrefix : enginePrefix;
        if ((isRunner || line.StartsWith(enginePrefix, StringComparison.OrdinalIgnoreCase)) &&
            int.TryParse(line.Substring(prefix.Length).Trim(), out pid) && pid > 0) return true;
        pid = 0;
        return false;
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

    internal static bool TryParseAppxWorkerMetadata(string line, out int pid)
    {
        const string prefix = "MINIAPPS_APPX_WORKER_PID:";
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(line.Substring(prefix.Length).Trim(), out pid) && pid > 0) return true;
        pid = 0;
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
        OptimizeStage.Preparing => "Đang chuẩn bị cấu hình Win11Debloat…",
        OptimizeStage.Applying => "Đang áp dụng cấu hình Default…",
        OptimizeStage.Overdue => "Một tác vụ gỡ package đã quá thời gian và vẫn đang được Windows xử lý.",
        OptimizeStage.Completed => "Đã tối ưu Windows.",
        OptimizeStage.Error => "Không thể hoàn tất tối ưu.",
        _ => "Sẵn sàng tối ưu Windows."
    };

    private static string StageLabel(OptimizeStage stage) => stage switch
    {
        OptimizeStage.Preparing => "chuẩn bị",
        OptimizeStage.Applying => "áp dụng",
        OptimizeStage.Overdue => "gỡ package quá thời gian",
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

    internal static string TaskCountSummary(IReadOnlyDictionary<string, OptimizeTaskEvent> states, object? sync = null)
    {
        string Build()
        {
            var expected = OptimizeTaskCatalog.Defaults().Select(task => task.Id).ToArray();
            var succeeded = expected.Count(id => states.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Done);
            var skipped = expected.Count(id => states.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Skip);
            var failed = expected.Count(id => states.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Error);
            var overdue = expected.Count(id => states.TryGetValue(id, out var value) && value == OptimizeTaskEvent.Overdue);
            var notRun = expected.Length - succeeded - skipped - failed - overdue;
            return $"Thành công {succeeded} · Bỏ qua {skipped} · Lỗi {failed} · Quá hạn {overdue} · Chưa chạy {notRun}.";
        }
        if (sync == null) return Build();
        lock (sync) return Build();
    }

    internal static bool IsAppxWorkerActive() => IsMutexHeld(() => new Mutex(false, AppxWorkerMutexName));

    private static bool IsMutexHeld(Func<Mutex> create)
    {
        using var gate = create();
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            return !acquired;
        }
        finally
        {
            if (acquired) gate.ReleaseMutex();
        }
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

    private sealed class OptimizeRunDiagnostics
    {
        public string Lifecycle { get; set; } = "";
        public string EngineCommit { get; set; } = "";
        public int? RunnerPid { get; set; }
        public int? EnginePid { get; set; }
        public int[] EnginePids { get; set; } = Array.Empty<int>();
        public int? AppxWorkerPid { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset? EndedAtUtc { get; set; }
        public DateTimeOffset? LastOutputAtUtc { get; set; }
        public DateTimeOffset? LastProtocolAtUtc { get; set; }
        public string Stage { get; set; } = "";
        public int? ExitCode { get; set; }
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int Overdue { get; set; }
        public int NotRun { get; set; }
        public string Failure { get; set; } = "";
    }
}

public sealed class OptimizePreviewService : IOptimizeService
{
    private readonly int delayMilliseconds;
    public OptimizePreviewService(int delayMilliseconds = 80) => this.delayMilliseconds = delayMilliseconds;

    public async Task<OptimizeResult> RunAsync(IProgress<OptimizeProgress> progress, CancellationToken cancellationToken)
    {
        var stages = new[] { OptimizeStage.Preparing, OptimizeStage.Applying };
        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new OptimizeProgress(stage, stage switch
            {
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
