using System.Diagnostics;
using System.IO;
using MiniApps.Services;
using MiniApps.Models;

internal static class DeploymentCancelTests
{
    // The test host executable: on net10 the assembly is a .dll next to its apphost.
    private static string Executable => Process.GetCurrentProcess().MainModule!.FileName;
    internal static void Fixture(string[] args)
    {
        File.WriteAllText(args[1], Process.GetCurrentProcess().Id.ToString());
        if (args[0] == "--job-parent")
        {
            using var child = Start("--job-leaf", args[1] + ".child");
            WaitFile(args[1] + ".child");
            // Exit while the descendant remains: the Job must retain it.
            return;
        }
        Thread.Sleep(TimeSpan.FromMinutes(2));
    }
    private static Process Start(string mode, string marker) => Process.Start(new ProcessStartInfo(Executable,
        ProcessCompatibility.JoinArguments([mode, marker])) { UseShellExecute = false, CreateNoWindow = true })!;
    private static void WaitFile(string path)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Fixture did not start: " + path);
            Thread.Sleep(20);
        }
    }
    private static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static void Bounded(Task task)
    {
        if (!task.Wait(TimeSpan.FromSeconds(15))) throw new Exception("Job cancellation did not complete in 15 seconds.");
        task.GetAwaiter().GetResult();
    }
    private static string Launch(string mode, string marker, bool wait, string? then = null) =>
        "$s=New-Object Diagnostics.ProcessStartInfo; $s.FileName=" + DeploymentService.Quote(Executable) +
        "; $s.Arguments=" + DeploymentService.Quote(ProcessCompatibility.JoinArguments([mode, marker])) +
        "; $s.UseShellExecute=$false; $s.CreateNoWindow=$true; $s.RedirectStandardOutput=$true; $s.RedirectStandardError=$true; " +
        "$p=[Diagnostics.Process]::Start($s); $null=$p.Handle; " + (wait ? "$p.WaitForExit(); " + (then ?? "exit $p.ExitCode") : "exit 0");
    internal static void Run(string root, Action<string, Action> check)
    {
        var log = new Sink();
        check("Owned runner preserves UTF-8 output, failure code and PowerShell errors", () =>
        {
            var work = Path.Combine(root, "job-output"); Directory.CreateDirectory(work);
            var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var group = new DeploymentProcessGroup(CancellationToken.None, log);
            var output = new Capture<string>(lines.Enqueue);
            var run = group.RunAsync("Write-Output 'Xin chào'; exit 37", work, output);
            Bounded(run);
            if (run.Result != 37 || !lines.Contains("Xin chào")) throw new Exception("Lost output or exit code.");
            var error = group.RunAsync("throw 'fixture-error'", work, output); Bounded(error);
            if (error.Result != 1 || !lines.Contains("fixture-error")) throw new Exception("Lost PowerShell error.");
        });
        check("Deployment service cancels real fixture scripts and drains their children", () =>
        {
            var work = Path.Combine(root, "job-service"); Directory.CreateDirectory(work);
            using var cancel = new CancellationTokenSource();
            var events = new System.Collections.Concurrent.ConcurrentQueue<DeploymentEvent>();
            var settings = Enumerable.Range(0, 2).Select(i => new WindowsSettingDefinition { Id = "fixture" + i, Name = "Fixture", Script = Launch("--job-leaf", Path.Combine(work, i + ".pid"), true) }).ToArray();
            var run = new DeploymentService(installed: _ => false, getWindowsBuild: () => 19045).RunAsync([], settings, work, new Capture<DeploymentEvent>(events.Enqueue), log, cancel.Token);
            try
            {
                for (var i = 0; i < 2; i++) WaitFile(Path.Combine(work, i + ".pid"));
                cancel.Cancel(); Bounded(run);
                if (events.Count(e => e.Finished && e.Status == "Đã hủy") != 2 || events.Any(e => e.Status == "Hoàn tất")) throw new Exception("Cancellation was not reported.");
                for (var i = 0; i < 2; i++) if (Alive(int.Parse(File.ReadAllText(Path.Combine(work, i + ".pid"))))) throw new Exception("Service returned before its child stopped.");
            }
            finally { cancel.Cancel(); Bounded(run); }
        });
        check("Force cancel kills owned descendants even after their parent exits, not an unrelated process", () =>
        {
            var work = Path.Combine(root, "job-tree"); Directory.CreateDirectory(work);
            var marker = Path.Combine(work, "parent.pid");
            using var unrelated = Start("--job-leaf", Path.Combine(work, "unrelated.pid"));
            using var cancel = new CancellationTokenSource();
            using var group = new DeploymentProcessGroup(cancel.Token, log);
            // The task is still running (its shell waits on), while the launcher parent has already exited.
            var run = group.RunAsync(Launch("--job-parent", marker, true, "Start-Sleep -Seconds 120"), work, log);
            try
            {
                WaitFile(marker + ".child");
                var child = int.Parse(File.ReadAllText(marker + ".child"));
                if (!Alive(child) || group.ActiveProcesses == 0) throw new Exception("Descendant not tracked.");
                cancel.Cancel(); Bounded(group.DrainCancellationAsync());
                if (Alive(child) || unrelated.HasExited || group.ActiveProcesses != 0) throw new Exception("Cancellation escaped its owned process group.");
                try { run.GetAwaiter().GetResult(); throw new Exception("Expected cancellation."); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                cancel.Cancel(); Bounded(group.DrainCancellationAsync());
                if (!unrelated.HasExited) { unrelated.Kill(); unrelated.WaitForExit(); }
            }
        });
        check("Force cancel stops concurrent active shells and returns cancellation instead of success", () =>
        {
            var work = Path.Combine(root, "job-parallel"); Directory.CreateDirectory(work);
            using var cancel = new CancellationTokenSource(); using var group = new DeploymentProcessGroup(cancel.Token, log);
            var tasks = Enumerable.Range(0, 3).Select(i => group.RunAsync(Launch("--job-leaf", Path.Combine(work, i + ".pid"), true), work, log)).ToArray();
            try
            {
                for (var i = 0; i < 3; i++) WaitFile(Path.Combine(work, i + ".pid"));
                cancel.Cancel(); Bounded(group.DrainCancellationAsync());
                foreach (var task in tasks)
                {
                    try { task.GetAwaiter().GetResult(); throw new Exception("Expected cancellation."); }
                    catch (OperationCanceledException) { }
                }
                for (var i = 0; i < 3; i++) if (Alive(int.Parse(File.ReadAllText(Path.Combine(work, i + ".pid"))))) throw new Exception("Child still running.");
            }
            finally { cancel.Cancel(); Bounded(group.DrainCancellationAsync()); }
        });
        check("Cancelled job never launches another command", () =>
        {
            var work = Path.Combine(root, "job-before-start"); Directory.CreateDirectory(work);
            var marker = Path.Combine(work, "should-not-exist");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            using var group = new DeploymentProcessGroup(cancel.Token, log);
            try { group.RunAsync("Set-Content -LiteralPath " + DeploymentService.Quote(marker) + " -Value bad", work, log).GetAwaiter().GetResult(); throw new Exception("Expected cancellation."); }
            catch (OperationCanceledException) { }
            if (File.Exists(marker) || group.ActiveProcesses != 0) throw new Exception("Cancelled group launched work.");
        });
        check("Force cancel spares apps left by a task that already finished", () =>
        {
            var work = Path.Combine(root, "job-finished-task"); Directory.CreateDirectory(work);
            var appMarker = Path.Combine(work, "app.pid"); var runningMarker = Path.Combine(work, "running.pid");
            using var cancel = new CancellationTokenSource();
            using var group = new DeploymentProcessGroup(cancel.Token, log);
            Process? app = null;
            try
            {
                Bounded(group.RunAsync(Launch("--job-leaf", appMarker, false), work, log));
                WaitFile(appMarker); app = Process.GetProcessById(int.Parse(File.ReadAllText(appMarker)));
                var running = group.RunAsync(Launch("--job-leaf", runningMarker, true), work, log);
                WaitFile(runningMarker);
                cancel.Cancel(); Bounded(group.DrainCancellationAsync());
                try { running.GetAwaiter().GetResult(); throw new Exception("Expected cancellation."); }
                catch (OperationCanceledException) { }
                if (app.HasExited) throw new Exception("Cancel killed an app left by a finished task.");
                if (Alive(int.Parse(File.ReadAllText(runningMarker)))) throw new Exception("Running task survived cancellation.");
            }
            finally
            {
                cancel.Cancel(); Bounded(group.DrainCancellationAsync());
                if (app != null) { if (!app.HasExited) { app.Kill(); app.WaitForExit(); } app.Dispose(); }
            }
        });
        check("Normal completion does not kill background applications", () =>
        {
            var work = Path.Combine(root, "job-normal"); Directory.CreateDirectory(work);
            var marker = Path.Combine(work, "app.pid");
            using var cancel = new CancellationTokenSource();
            var group = new DeploymentProcessGroup(cancel.Token, log);
            Process? app = null;
            var completed = false;
            try
            {
                Bounded(group.RunAsync(Launch("--job-leaf", marker, false), work, log));
                WaitFile(marker); app = Process.GetProcessById(int.Parse(File.ReadAllText(marker)));
                group.Dispose();
                if (app.HasExited) throw new Exception("Normal completion killed the launched app.");
                completed = true;
            }
            finally
            {
                if (!completed && app == null) { cancel.Cancel(); Bounded(group.DrainCancellationAsync()); }
                group.Dispose();
                if (app != null) { if (!app.HasExited) { app.Kill(); app.WaitForExit(); } app.Dispose(); }
            }
        });
    }
    private sealed class Sink : IProgress<string> { public void Report(string value) { } }
    private sealed class Capture<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
