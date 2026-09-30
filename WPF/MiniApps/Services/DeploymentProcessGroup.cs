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
    internal async Task<int> RunAsync(string command, string workDir, IProgress<string> outputLog)
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
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
