using System.Diagnostics;
using System.Text;

namespace MiniApps.Services;

public sealed record ExtensionRunResult(int ExitCode, string LogPath);

// Runs one EXTEND add-on: its PowerShell script (embedded in the assembly) in a hidden
// Windows PowerShell 5.1 process, with every output line reported and kept in a log file.
public sealed class ExtensionService(Func<string, string, IProgress<string>, Task<int>>? runScript = null)
{
    // Tests point this at a temporary folder so they leave no logs on the machine.
    internal static string LogDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps", "ExtendLogs");

    // The script to run first, then any script it calls from its own folder.
    internal static IReadOnlyList<string> ScriptsFor(string extensionId) => extensionId switch
    {
        "debloat" => ["Invoke-Win11Debloat.ps1"],
        "cpp" => ["Install-CppEnvironment.ps1", "Update-Winget.ps1"],
        "sharelan" => ["Share-LAN.ps1"],
        _ => throw new ArgumentException("Phần mở rộng không được hỗ trợ: " + extensionId, nameof(extensionId))
    };

    private readonly Func<string, string, IProgress<string>, Task<int>> runScript = runScript ?? RunScriptFileAsync;

    // Interactive tools are kept here and overwritten on each launch, so the file is still
    // there for as long as the tool's own window stays open.
    internal static string ToolsDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps", "Tools");

    // Writes an interactive tool's script and returns its path; the tool is opened separately.
    internal static string WriteTool(string extensionId)
    {
        var script = ScriptsFor(extensionId)[0];
        Directory.CreateDirectory(ToolsDirectory);
        var path = Path.Combine(ToolsDirectory, script);
        using var resource = typeof(ExtensionService).Assembly.GetManifestResourceStream("MiniApps.Scripts." + script)
            ?? throw new InvalidOperationException("Thiếu script nhúng: " + script);
        using var file = File.Create(path);
        resource.CopyTo(file);
        return path;
    }

    // Opens an interactive tool in its own visible PowerShell window. MiniApps already runs
    // as Administrator, so the window inherits that and the tool asks for no second UAC prompt.
    public static void LaunchTool(string extensionId)
    {
        var path = WriteTool(extensionId);
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            Arguments = ProcessCompatibility.JoinArguments(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path }),
            UseShellExecute = true,
            WorkingDirectory = ToolsDirectory
        });
    }

    public async Task<ExtensionRunResult> RunAsync(string extensionId, IProgress<string> progress)
    {
        var scripts = ScriptsFor(extensionId);
        var workDir = Path.Combine(WorkFolderCleaner.DefaultRoot, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(LogDirectory);
        var logPath = Path.Combine(LogDirectory, $"{extensionId}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}.log");
        using var logFile = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        var sync = new object();
        var log = new LineProgress(line =>
        {
            lock (sync) logFile.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            progress.Report(line);
        });
        try
        {
            foreach (var script in scripts)
            {
                using var resource = typeof(ExtensionService).Assembly.GetManifestResourceStream("MiniApps.Scripts." + script)
                    ?? throw new InvalidOperationException("Thiếu script nhúng: " + script);
                using var file = File.Create(Path.Combine(workDir, script));
                await resource.CopyToAsync(file);
            }
            var code = await runScript(Path.Combine(workDir, scripts[0]), workDir, log);
            // For the log only: the card already shows the result, and this line would replace
            // the script's own last progress line there.
            lock (sync) logFile.WriteLine($"[{DateTime.Now:HH:mm:ss}] Kết thúc với mã {code}.");
            return new(code, logPath);
        }
        finally
        {
            // Anything still held is left for WorkFolderCleaner at the next start.
            try { Directory.Delete(workDir, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static async Task<int> RunScriptFileAsync(string script, string workDir, IProgress<string> log)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            Arguments = ProcessCompatibility.JoinArguments(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }),
            UseShellExecute = false,
            CreateNoWindow = true,
            // Input is redirected and closed at once: a "press any key" left in any script
            // then fails and moves on instead of waiting forever in a window nobody can see.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir
        };
        using var process = Process.Start(start) ?? throw new IOException("Không khởi chạy được PowerShell.");
        process.StandardInput.Close();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line) log.Report(line);
        }
        var output = ReadAsync(process.StandardOutput);
        var error = ReadAsync(process.StandardError);
        // No timeout and no kill: Debloat and the C++ toolchain take minutes, and stopping
        // either half-way leaves the machine in a worse state than letting it finish.
        await ProcessCompatibility.WaitForExitAsync(process);
        await Task.WhenAll(output, error);
        return process.ExitCode;
    }

    private sealed class LineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
