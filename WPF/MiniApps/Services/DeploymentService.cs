using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MiniApps.Models;

namespace MiniApps.Services;
public sealed record DeploymentEvent(string Id, string Status, double Progress = 0, bool Finished = false, bool Failed = false);

public sealed class DeploymentService
{
    // Held for the whole of an install run, by every MiniApps window.
    public const string DeploymentLockName = "Global\\MiniApps.Deployment";
    private static readonly HttpClient DefaultHttp = CreateDefaultHttpClient();
    private readonly HttpClient http;
    private readonly Func<string, string, IProgress<string>, Task<int>> run;
    private readonly Func<AppDefinition, bool>? installedOverride;
    private readonly Func<InstalledSoftwareSnapshot> loadInstalledSoftware;
    private readonly Func<AppDefinition, InstalledSoftwareSnapshot, SoftwareDetectionResult> detectInstalledSoftware;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<int> getWindowsBuild;
    private readonly bool useOwnedProcesses;
    public DeploymentService(HttpClient? http = null, Func<string, string, IProgress<string>, Task<int>>? run = null,
        Func<AppDefinition, bool>? installed = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<int>? getWindowsBuild = null
        , Func<InstalledSoftwareSnapshot>? loadInstalledSoftware = null,
        Func<AppDefinition, InstalledSoftwareSnapshot, SoftwareDetectionResult>? detectInstalledSoftware = null
        )
    {
        this.http = http ?? DefaultHttp;
        this.run = run ?? RunPowerShellAsync;
        useOwnedProcesses = run == null;
        installedOverride = installed;
        this.loadInstalledSoftware = loadInstalledSoftware ?? InstalledSoftwareDetector.Capture;
        this.detectInstalledSoftware = detectInstalledSoftware ?? InstalledSoftwareDetector.Detect;
        this.delay = delay ?? Task.Delay;
        this.getWindowsBuild = getWindowsBuild ?? (() => WindowsCompatibility.CurrentBuild);
    }
    private static HttpClient CreateDefaultHttpClient()
    {
        // .NET Framework otherwise inherits a two-connection-per-host limit, which serializes the catalog.
        return new HttpClient(CreateDefaultHttpHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal static HttpClientHandler CreateDefaultHttpHandler() => new() { MaxConnectionsPerServer = int.MaxValue };
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    public async Task RunAsync(IReadOnlyList<AppDefinition> apps, IReadOnlyList<WindowsSettingDefinition> options, string workDir,
        IProgress<DeploymentEvent> events, IProgress<string> log, CancellationToken token)
    {
        Catalog.Validate(apps);
        WindowsSettingsCatalog.Validate(options);
        Directory.CreateDirectory(workDir);
        using var processGroup = useOwnedProcesses ? new DeploymentProcessGroup(token, log) : null;
        async Task<int> RunOwned(string command, string? helperFolder = null, string label = "")
        {
            token.ThrowIfCancellationRequested();
            var code = processGroup != null ? await processGroup.RunAsync(command, workDir, log, helperFolder, label) : await run(command, workDir, log);
            token.ThrowIfCancellationRequested();
            return code;
        }
        var windowsBuild = options.Count == 0 ? WindowsCompatibility.MinimumWindowsBuild : getWindowsBuild();
        using var msiInstallSlot = new SemaphoreSlim(1);
        Task<InstalledSoftwareSnapshot?> snapshotTask = installedOverride == null
            ? Task.Run<InstalledSoftwareSnapshot?>(() => loadInstalledSoftware(), token)
            : Task.FromResult<InstalledSoftwareSnapshot?>(null);
        var launchSnapshot = new Lazy<Task<InstalledSoftwareSnapshot>>(
            () => Task.Run(loadInstalledSoftware, token), LazyThreadSafetyMode.ExecutionAndPublication);
        var decisionLogLock = new object();
        var decisionLogPath = installedOverride == null ? CreateSmartSkipLogPath() : "";
        var appTasks = apps.Select(async app =>
        {
            string? installer = null;
            try
            {
                token.ThrowIfCancellationRequested();
                events.Report(new(app.Id, "Đang kiểm tra"));
                var firstDetection = installedOverride != null
                    ? new SoftwareDetectionResult(installedOverride(app) ? SoftwareDetectionState.Installed : SoftwareDetectionState.NotInstalled,
                        "Kết quả từ bộ nhận diện được truyền vào.")
                    : detectInstalledSoftware(app, (await snapshotTask)!);
                ReportDetection(app, firstDetection, "trước tải");
                if (firstDetection.State == SoftwareDetectionState.Installed)
                {
                    events.Report(new(app.Id, "Đã cài · bỏ qua", 100, true));
                    return;
                }
                if (firstDetection.State == SoftwareDetectionState.Unknown)
                {
                    events.Report(new(app.Id, "Không xác minh được · không cài", 0, true, true));
                    return;
                }
                events.Report(new(app.Id, "Chờ tải"));
                var path = installer = await DownloadAsync(app, workDir, events, log, token);
                events.Report(new(app.Id, "Chờ cài đặt", 100));
                token.ThrowIfCancellationRequested();
                var launchDetection = installedOverride != null
                    ? new SoftwareDetectionResult(installedOverride(app) ? SoftwareDetectionState.Installed : SoftwareDetectionState.NotInstalled,
                        "Kết quả từ bộ nhận diện được truyền vào.")
                    : detectInstalledSoftware(app, await launchSnapshot.Value);
                ReportDetection(app, launchDetection, "trước chạy bộ cài");
                if (launchDetection.State == SoftwareDetectionState.Installed)
                {
                    events.Report(new(app.Id, "Đã cài trong lúc chờ · bỏ qua", 100, true));
                    return;
                }
                if (launchDetection.State == SoftwareDetectionState.Unknown)
                {
                    events.Report(new(app.Id, "Không xác minh được · không cài", 0, true, true));
                    return;
                }
                var isMsi = Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                var file = isMsi ? Path.Combine(Environment.SystemDirectory, "msiexec.exe") : path;
                var args = isMsi ? $"/i \"{path}\" {app.Arguments}" : app.Arguments;
                // Not -Wait: it also waits for the app an installer opens (Zalo, WPS...) until the user closes it.
                // The shell waits for the installer itself (reading Handle first keeps its exit code); the process
                // group then waits for stages still running from the work folder, unless WaitInstallerOnly.
                var command = $"$p = Start-Process -FilePath {Quote(file)} " +
                    (string.IsNullOrWhiteSpace(args) ? "" : $"-ArgumentList {Quote(args)} ") +
                    "-PassThru -ErrorAction Stop; $null = $p.Handle; $p.WaitForExit(); " +
                    "if ($null -eq $p.ExitCode) { throw 'Installer returned no exit code' }; exit $p.ExitCode";
                int code = 1618;
                for (var attempt = 0; attempt <= 3; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (isMsi) await msiInstallSlot.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        events.Report(new(app.Id, string.IsNullOrWhiteSpace(app.Arguments) ? "Đang cài · hãy thao tác trong bộ cài" : "Đang cài đặt", 100));
                        log.Report(app.WaitInstallerOnly ? $"{app.Name}: bắt đầu bộ cài. Chờ bộ cài kết thúc, không chờ ứng dụng nó mở." : $"{app.Name}: bắt đầu bộ cài. Chờ bộ cài và tiến trình phụ trong thư mục tạm, không chờ ứng dụng nó mở.");
                        // Explicit cancellation terminates the task's owned job; it cannot roll back changes.
                        code = await RunOwned(command, app.WaitInstallerOnly ? null : workDir, app.Name);
                    }
                    finally
                    {
                        if (isMsi) msiInstallSlot.Release();
                    }
                    if (code != 1618 || attempt == 3) break;
                    token.ThrowIfCancellationRequested();
                    var delay = TimeSpan.FromSeconds((attempt + 1) * 5);
                    events.Report(new(app.Id, $"Chờ cài đặt · Windows Installer đang bận · thử lại sau {delay.TotalSeconds:0} giây", 100));
                    log.Report($"{app.Name}: bộ cài trả mã 1618; thử lại lần {attempt + 1}/3 sau {delay.TotalSeconds:0} giây.");
                    await this.delay(delay, token);
                }
                if (code != 0 && code != 3010 && code != 1641)
                {
                    log.Report($"{app.Name}: bộ cài trả mã {code}.");
                    events.Report(new(app.Id, $"Thất bại · mã {code}", 0, true, true));
                    return;
                }
                events.Report(new(app.Id, code == 0 ? "Hoàn tất" : "Hoàn tất · cần khởi động lại", 100, true));
            }
            catch (OperationCanceledException) { events.Report(new(app.Id, "Đã hủy", 0, true)); }
            catch (Exception ex) { log.Report($"{app.Name}: {ex.Message}"); events.Report(new(app.Id, "Thất bại", 0, true, true)); }
            finally
            {
                // Free the disk as soon as this app is done instead of holding every installer until the run ends.
                // A file still held by the installer stays; the caller removes the whole work folder afterwards.
                if (installer != null)
                {
                    try { File.Delete(installer); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Report($"{app.Name}: chưa xóa được bộ cài ({ex.Message}); sẽ dọn khi hết lượt."); }
                }
            }
        }).ToArray();
        var settingTasks = options.Select(async option =>
        {
            if (token.IsCancellationRequested) { events.Report(new(option.TaskId, "Đã hủy", 0, true)); return; }
            if (!WindowsCompatibility.Supports(option, windowsBuild))
            {
                log.Report($"Windows/{option.Name}: bỏ qua trên build {windowsBuild}; yêu cầu build {WindowsCompatibility.MinimumBuildFor(option)} trở lên.");
                events.Report(new(option.TaskId, "Bỏ qua · Windows không hỗ trợ", 100, true));
                return;
            }
            try
            {
                events.Report(new(option.TaskId, "Đang áp dụng"));
                // The exact command shown and saved in Setting is used for EVERY definition.
                var script = Path.Combine(workDir, $"setting-{option.Id}.ps1");
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    File.WriteAllText(script, option.Script, new UTF8Encoding(true));
                }, token);
                var command = $"& {Quote(script)}; if (-not $?) {{ throw 'Windows setting failed.' }}";
                var code = await RunOwned(command);
                if (code != 0) throw new InvalidOperationException($"Mã lỗi {code}.");
                events.Report(new(option.TaskId, "Hoàn tất", 100, true));
            }
            catch (OperationCanceledException) { events.Report(new(option.TaskId, "Đã hủy", 0, true)); }
            catch (Exception ex) { log.Report($"Windows/{option.Name}: {ex.Message}"); events.Report(new(option.TaskId, "Thất bại", 0, true, true)); }
        }).ToArray();
        try { await Task.WhenAll(appTasks.Concat(settingTasks)); }
        finally
        {
            if (processGroup != null) await processGroup.DrainCancellationAsync();
        }

        void ReportDetection(AppDefinition app, SoftwareDetectionResult result, string phase)
        {
            var evidence = string.IsNullOrWhiteSpace(result.Evidence) ? "" : $" Bằng chứng: {result.Evidence}.";
            var line = $"Smart Skip/{app.Id} ({phase}): {result.State}. {result.Reason}{evidence}";
            log.Report(line);
            if (decisionLogPath.Length == 0) return;
            try
            {
                lock (decisionLogLock)
                    File.AppendAllText(decisionLogPath, $"[{DateTimeOffset.Now:O}] {line}{Environment.NewLine}", new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // Tests point this at a temporary folder so they leave no logs on the machine.
    internal static string InstallLogDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps", "InstallLogs");

    private static string CreateSmartSkipLogPath()
    {
        try
        {
            var directory = InstallLogDirectory;
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"smart-skip-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        }
        catch { return ""; }
    }

    // Files this large are fetched as parallel byte ranges when the server supports them.
    internal const long SegmentThreshold = 16L * 1024 * 1024;
    internal const int SegmentCount = 4;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(90);
    // A source with another one after it gets less time to answer before the next is tried.
    internal static TimeSpan FallbackResponseTimeout { get; set; } = TimeSpan.FromSeconds(20);

    private async Task<string> DownloadAsync(AppDefinition app, string workDir, IProgress<DeploymentEvent> events, IProgress<string> log, CancellationToken token)
    {
        var path = Path.Combine(workDir, app.Id + Path.GetExtension(new Uri(app.Url).AbsolutePath));
        var sources = Catalog.DownloadSources(app.Url);
        for (var i = 0; ; i++)
        {
            var last = i == sources.Count - 1;
            try
            {
                await DownloadFromAsync(app, sources[i], path, last, events, log, token);
                return path;
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            catch (Exception ex) when (!last)
            {
                log.Report($"{app.Name}: không tải được từ {new Uri(sources[i]).Host} ({ex.Message}); chuyển sang {new Uri(sources[i + 1]).Host}.");
            }
        }
    }

    // Interrupted transfers resume inside FetchAsync. A file that fails its check is fetched again from
    // the same place only when no other source is left.
    private async Task DownloadFromAsync(AppDefinition app, string url, string path, bool last, IProgress<DeploymentEvent> events, IProgress<string> log, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await FetchAsync(app, url, path, last ? StallTimeout : FallbackResponseTimeout, events, log, token);
                await VerifyFileAsync(path, app.Sha256, token);
                return;
            }
            catch (InvalidDataException ex) when (last && attempt < 2 && !token.IsCancellationRequested)
            {
                log.Report($"{app.Name}: file tải về không hợp lệ ({ex.Message}); tải lại từ đầu.");
            }
        }
    }

    // The first request asks for "bytes=0-". A server without range support answers 200 with the whole
    // file, which is then read as before; a 206 gives the size, so the rest can be split and resumed.
    private async Task FetchAsync(AppDefinition app, string url, string path, TimeSpan responseTimeout, IProgress<DeploymentEvent> events, IProgress<string> log, CancellationToken token)
    {
        var progress = new DownloadProgress(app.Id, events);
        HttpResponseMessage? first = null;
        try
        {
            first = await GetRangeAsync(url, 0, null, token, responseTimeout);
            var total = first.StatusCode == System.Net.HttpStatusCode.PartialContent ? TotalLength(first, 0) : null;
            if (total == null)
            {
                await FetchWholeAsync(app, url, path, first, progress, log, token);
                first = null;
                return;
            }
            progress.Total = total.Value;
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) file.SetLength(total.Value);
            var count = total.Value >= SegmentThreshold ? SegmentCount : 1;
            var size = (total.Value + count - 1) / count;
            using var group = CancellationTokenSource.CreateLinkedTokenSource(token);
            var segments = new Task[count];
            Exception? cause = null;
            for (var i = 0; i < count; i++)
            {
                var start = i * size;
                var end = Math.Min(total.Value, start + size) - 1;
                var response = i == 0 ? first : null;
                segments[i] = Task.Run(async () =>
                {
                    try { await FetchSegmentAsync(app, url, path, start, end, response, progress, log, group.Token); }
                    catch (Exception ex) { Interlocked.CompareExchange(ref cause, ex, null); group.Cancel(); throw; }
                });
            }
            first = null;
            if (count > 1) log.Report($"{app.Name}: tải {count} luồng song song ({total.Value / 1048576d:0.0} MB).");
            // A failed segment cancels its siblings; report the failure itself, not their cancellation.
            try { await Task.WhenAll(segments); }
            catch when (cause != null && !token.IsCancellationRequested) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw(); }
        }
        finally { first?.Dispose(); }
    }

    // One byte range; a dropped connection resumes from the last byte written. It gives up after three
    // attempts in a row that added nothing.
    private async Task FetchSegmentAsync(AppDefinition app, string url, string path, long start, long end, HttpResponseMessage? first,
        DownloadProgress progress, IProgress<string> log, CancellationToken token)
    {
        var position = start;
        var idle = 0;
        while (position <= end)
        {
            var before = position;
            try
            {
                using var response = first ?? await GetRangeAsync(url, position, end, token);
                first = null;
                if (response.StatusCode != System.Net.HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != position)
                    throw new IOException("Máy chủ không tải tiếp được từ vị trí đã dừng.");
                using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 81920, true);
                file.Position = position;
                await CopyAsync(response, file, end - position + 1, read => { position += read; progress.Add(read); }, token);
                if (position <= end) throw new IOException("Kết nối đóng trước khi tải xong.");
            }
            catch (Exception ex) when (!token.IsCancellationRequested && position <= end)
            {
                first?.Dispose(); first = null;
                idle = position > before ? 1 : idle + 1;
                if (idle >= 3) throw;
                log.Report($"{app.Name}: tải bị gián đoạn ({ex.Message}); tải tiếp từ {position / 1048576d:0.0} MB.");
                await Task.Delay(TimeSpan.FromSeconds(idle * 2), token);
            }
        }
    }

    // A server without range support: each retry starts the whole file again.
    private async Task FetchWholeAsync(AppDefinition app, string url, string path, HttpResponseMessage? first, DownloadProgress progress, IProgress<string> log, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = first ?? await GetRangeAsync(url, null, null, token);
                first = null;
                progress.Reset(response.Content.Headers.ContentLength);
                long received = 0;
                using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    await CopyAsync(response, file, null, read => { received += read; progress.Add(read); }, token);
                var length = response.Content.Headers.ContentLength;
                if (length.HasValue && received != length.Value) throw new IOException("File tải chưa đầy đủ.");
                return;
            }
            catch (Exception ex) when (!token.IsCancellationRequested && attempt < 3)
            {
                first?.Dispose(); first = null;
                log.Report($"{app.Name}: tải lần {attempt} lỗi ({ex.Message}); máy chủ không hỗ trợ tải tiếp, thử lại từ đầu.");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), token);
            }
        }
    }

    // from == null sends no Range header. Headers that never arrive count as a stall.
    private async Task<HttpResponseMessage> GetRangeAsync(string url, long? from, long? to, CancellationToken token, TimeSpan? responseTimeout = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (from.HasValue) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
        var timeout = responseTimeout ?? StallTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException($"Máy chủ không phản hồi trong {timeout.TotalSeconds:0} giây."); }
        try
        {
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Chuyển hướng tải không an toàn.");
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    private static long? TotalLength(HttpResponseMessage response, long from)
    {
        var range = response.Content.Headers.ContentRange;
        return range?.Length is long length && range.From == from && length > 0 ? length : null;
    }

    // Copies until limit bytes (or the end of the body when limit is null). Each read must make
    // progress within the stall timeout.
    private static async Task CopyAsync(HttpResponseMessage response, FileStream output, long? limit, Action<int> wrote, CancellationToken token)
    {
        using var input = await response.Content.ReadAsStreamAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        using (deadline.Token.Register(() =>
        {
            // Framework response streams do not consistently observe cancellation while a read is stalled.
            try { input.Dispose(); } catch { }
            try { response.Dispose(); } catch { }
        }))
        {
            var buffer = new byte[81920];
            var remaining = limit;
            try
            {
                while (remaining is not <= 0)
                {
                    deadline.CancelAfter(StallTimeout);
                    var want = remaining.HasValue ? (int)Math.Min(buffer.Length, remaining.Value) : buffer.Length;
                    var size = await input.ReadAsync(buffer, 0, want, deadline.Token);
                    if (size == 0) break;
                    await output.WriteAsync(buffer, 0, size, deadline.Token);
                    if (remaining.HasValue) remaining -= size;
                    wrote(size);
                }
            }
            catch (Exception) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
            {
                throw new TimeoutException("Không nhận được dữ liệu trong 90 giây.");
            }
        }
    }

    // Shared by the segments of one file; reports at most every 250 ms.
    private sealed class DownloadProgress(string id, IProgress<DeploymentEvent> events)
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long received;
        public long? Total { get; set; }
        public void Reset(long? total) { Interlocked.Exchange(ref received, 0); Total = total; }
        public void Add(int count)
        {
            var now = Interlocked.Add(ref received, count);
            lock (clock)
            {
                if (clock.ElapsedMilliseconds < 250) return;
                clock.Restart();
            }
            var percent = Total > 0 ? now * 100d / Total.Value : 0;
            events.Report(new(id, $"Đang tải · {now / 1048576d:0.0} MB", percent));
        }
    }

    public static async Task VerifyFileAsync(string path, string expectedHash, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[8];
        var count = await stream.ReadAsync(header, 0, header.Length, token);
        var valid = Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase)
            ? count == 8 && header.SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })
            : count >= 2 && header[0] == 0x4D && header[1] == 0x5A;
        if (!valid) throw new InvalidDataException("Nội dung tải về không phải bộ cài Windows.");
        if (expectedHash.Length == 0) return; // Format check is not authenticity verification; UI warns for custom URLs.
        stream.Position = 0;
        string actual;
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            int size;
            while ((size = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
            {
                token.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, size, buffer, 0);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            actual = BitConverter.ToString(sha.Hash!).Replace("-", "");
        }
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 không khớp; không chạy file.");
    }

    public static bool IsInstalled(AppDefinition app)
    {
        return InstalledSoftwareDetector.Detect(app, InstalledSoftwareDetector.Capture()).State == SoftwareDetectionState.Installed;
    }

    internal static bool InstalledNameMatches(AppDefinition app, string displayName)
    {
        return InstalledSoftwareDetector.InstalledNameMatches(app, displayName);
    }

    internal static async Task<int> RunPowerShellAsync(string command, string workDir, IProgress<string> log)
        => await RunPowerShellAsync(command, workDir, log,
            "Tác vụ đã chạy hơn 30 phút. Tiếp tục chờ để không làm gián đoạn tiến trình đang hoạt động.");

    internal static async Task<int> RunPowerShellAsync(string command, string workDir, IProgress<string> log, string prolongedMessage)
    {
        var wrapper = "$ErrorActionPreference='Stop'; try { " + command + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = workDir
        };
        start.Arguments = ProcessCompatibility.JoinArguments(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapper)) });
        start.EnvironmentVariables["TEMP"] = workDir;
        start.EnvironmentVariables["TMP"] = workDir;
        using var process = Process.Start(start) ?? throw new IOException("Không khởi chạy được PowerShell.");
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line) log.Report(line);
        }
        var output = ReadAsync(process.StandardOutput);
        var error = ReadAsync(process.StandardError);
        var exit = ProcessCompatibility.WaitForExitAsync(process);
        if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromMinutes(30))) != exit)
            log.Report(prolongedMessage);
        await exit;
        await Task.WhenAll(output, error);
        return process.ExitCode;
    }
}
