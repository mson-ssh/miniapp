using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniApps.Services;

// One unnamed Windows Job per task (installer or Windows Setting) of a deployment. Assign each gated
// PowerShell BEFORE allowing user commands to execute. Descendants inherit membership, even if their
// parent exits. Cancel terminates only the jobs of tasks still running: apps left by a task that has
// already finished (e.g. EVKey) are released and survive. No kill-by-name, PID scan, or termination
// of shared Windows services.
internal sealed class DeploymentProcessGroup : IDisposable
{
    private readonly List<IntPtr> jobs = new();
    private readonly object sync = new();
    private readonly CancellationToken token;
    private readonly CancellationTokenRegistration registration;
    private readonly IProgress<string> log;
    private bool cancelling;
    private int disposed;
    internal DeploymentProcessGroup(CancellationToken token, IProgress<string> log)
    {
        this.token = token; this.log = log;
        registration = token.Register(Cancel);
    }
    private void Cancel()
    {
        lock (sync)
        {
            cancelling = true;
            foreach (var job in jobs)
                if (!TerminateJobObject(job, 1223)) log.Report("Hủy: Windows chưa dừng được nhóm tiến trình, mã " + Marshal.GetLastWin32Error());
        }
    }
    internal uint ActiveProcesses
    {
        get
        {
            lock (sync)
            {
                uint total = 0;
                foreach (var job in jobs)
                {
                    if (!QueryInformationJobObject(job, 1, out var info, Marshal.SizeOf(typeof(Accounting)), IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    total += info.ActiveProcesses;
                }
                return total;
            }
        }
    }
    internal async Task DrainCancellationAsync()
    {
        if (!token.IsCancellationRequested) return;
        var clock = Stopwatch.StartNew();
        while (ActiveProcesses != 0)
        {
            Cancel();
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
            {
                log.Report("Đang hủy: còn tiến trình thuộc lượt cài chưa thoát; chưa nhả khóa hoặc dọn file.");
                clock.Restart();
            }
            await Task.Delay(100).ConfigureAwait(false);
        }
    }
    // helperFolder: after the shell exits, keep waiting for processes of this task that run from that
    // folder (an installer's self-extracted stages), but not for apps it started elsewhere.
    internal async Task<int> RunAsync(string command, string workDir, IProgress<string> outputLog, string? helperFolder = null, string label = "")
    {
        token.ThrowIfCancellationRequested();
        var gateName = "Local\\MiniApps.Start." + Guid.NewGuid().ToString("N");
        using var gate = new EventWaitHandle(false, EventResetMode.ManualReset, gateName);
        var wrapper = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); try { $g=[Threading.EventWaitHandle]::OpenExisting('" + gateName +
            "'); if (-not $g.WaitOne(60000)) { exit 1223 }; $g.Dispose(); " + command +
            " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            Arguments = ProcessCompatibility.JoinArguments(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapper))]),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, WorkingDirectory = workDir
        };
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        start.EnvironmentVariables["TEMP"] = workDir;
        start.EnvironmentVariables["TMP"] = workDir;
        Process process;
        IntPtr job;
        lock (sync)
        {
            token.ThrowIfCancellationRequested();
            if (cancelling) throw new OperationCanceledException(token);
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            jobs.Add(job);
            process = Process.Start(start) ?? throw new IOException("Không khởi chạy được PowerShell.");
            try
            {
                if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Không quản lý được cây tiến trình; chưa chạy tác vụ.");
                process.StandardInput.Close();
                gate.Set();
            }
            catch
            {
                // Only our newly created, gated shell; no installer has started yet.
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } } finally { process.Dispose(); }
                throw;
            }
        }
        using (process)
        {
            var stdout = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderr = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += (_, e) => { if (e.Data == null) stdout.TrySetResult(true); else outputLog.Report(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data == null) stderr.TrySetResult(true); else outputLog.Report(e.Data); };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var exit = ProcessCompatibility.WaitForExitAsync(process);
            if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromMinutes(30))).ConfigureAwait(false) != exit)
                outputLog.Report("Tác vụ chạy quá 30 phút; có thể bấm HỦY để dừng cây tiến trình của lượt cài.");
            await exit.ConfigureAwait(false);
            // A background app can inherit a pipe handle after its launcher exits.
            // Drain final output, but do not turn WaitInstallerOnly into a descendant wait.
            var drained = Task.WhenAll(stdout.Task, stderr.Task);
            if (await Task.WhenAny(drained, Task.Delay(500)).ConfigureAwait(false) != drained)
            {
                process.CancelOutputRead(); process.CancelErrorRead();
            }
            if (helperFolder != null) await WaitForHelpersAsync(job, helperFolder, label, outputLog).ConfigureAwait(false);
            lock (sync)
            {
                // Finished before any cancel: release the job so a later HỦY leaves its apps running.
                // Once cancelling, keep it tracked until DrainCancellationAsync sees it empty.
                if (!cancelling && !token.IsCancellationRequested) { jobs.Remove(job); CloseHandle(job); }
            }
            token.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
    }
    // An installer that opens the installed app (or leaves an updater running) must not hold the run
    // until the user closes that app: only processes still running from the work folder are awaited.
    private async Task WaitForHelpersAsync(IntPtr job, string helperFolder, string label, IProgress<string> outputLog)
    {
        var root = LongPath(helperFolder).TrimEnd('\\') + "\\";
        var announced = false;
        while (!token.IsCancellationRequested)
        {
            var images = ProcessImages(job);
            var helpers = images.Where(image => image.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToList();
            if (helpers.Count == 0)
            {
                var left = images.Select(Path.GetFileName)
                    .Where(name => !name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (left.Count > 0) outputLog.Report($"{label}: bộ cài đã thoát; không chờ tiến trình nó để lại: {string.Join(", ", left)}.");
                return;
            }
            if (!announced)
            {
                announced = true;
                outputLog.Report($"{label}: bộ cài chính đã thoát; chờ tiến trình phụ trong thư mục tạm: {string.Join(", ", helpers.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase))}.");
            }
            await Task.Delay(500).ConfigureAwait(false);
        }
    }
    // Image paths of the processes still in the job; a process that exits or cannot be opened is skipped.
    private static List<string> ProcessImages(IntPtr job)
    {
        var images = new List<string>();
        var capacity = 64;
        while (true)
        {
            var size = 2 * sizeof(uint) + capacity * IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!QueryInformationJobObject(job, 3, buffer, size, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 234 && capacity < 65536) { capacity *= 4; continue; } // ERROR_MORE_DATA
                    throw new Win32Exception(error);
                }
                var count = Marshal.ReadInt32(buffer, sizeof(uint));
                for (var i = 0; i < count; i++)
                {
                    var pid = (uint)Marshal.ReadIntPtr(buffer, 2 * sizeof(uint) + i * IntPtr.Size).ToInt64();
                    var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
                    if (handle == IntPtr.Zero) continue;
                    try
                    {
                        var path = new StringBuilder(32768); var length = path.Capacity;
                        if (QueryFullProcessImageName(handle, 0, path, ref length)) images.Add(path.ToString(0, length));
                    }
                    finally { CloseHandle(handle); }
                }
                return images;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    // %TEMP% may hold 8.3 names (C:\Users\NGUYEN~1\...); process image paths are always long.
    private static string LongPath(string path)
    {
        var full = Path.GetFullPath(path);
        var result = new StringBuilder(32768);
        var length = GetLongPathName(full, result, result.Capacity);
        return length > 0 && length < result.Capacity ? result.ToString(0, (int)length) : full;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        registration.Dispose();
        // No KILL_ON_JOB_CLOSE: normal completion must leave launched apps (e.g. EVKey) alive.
        lock (sync)
        {
            foreach (var job in jobs) CloseHandle(job);
            jobs.Clear();
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUser, TotalKernel, PeriodUser, PeriodKernel;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out Accounting info, int size, IntPtr length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, int size, IntPtr length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string path, StringBuilder result, int size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
