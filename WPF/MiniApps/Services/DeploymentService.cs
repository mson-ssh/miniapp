using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MiniApps.Models;

namespace MiniApps.Services;
public sealed record DeploymentEvent(string Id, string Status, double Progress = 0, bool Finished = false, bool Failed = false);

public sealed class DeploymentService
{
    private static readonly HttpClient DefaultHttp = CreateDefaultHttpClient();
    private readonly HttpClient http;
    private readonly Func<string, string, IProgress<string>, Task<int>> run;
    private readonly Func<AppDefinition, bool> installed;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Func<int> getWindowsBuild;
    public DeploymentService(HttpClient? http = null, Func<string, string, IProgress<string>, Task<int>>? run = null,
        Func<AppDefinition, bool>? installed = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<int>? getWindowsBuild = null)
    {
        this.http = http ?? DefaultHttp;
        this.run = run ?? RunPowerShellAsync;
        this.installed = installed ?? IsInstalled;
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
        var windowsBuild = options.Count == 0 ? WindowsCompatibility.MinimumWindowsBuild : getWindowsBuild();
        using var msiInstallSlot = new SemaphoreSlim(1);
        var appTasks = apps.Select(async app =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (await Task.Run(() => installed(app), token)) { events.Report(new(app.Id, "Đã cài · bỏ qua", 100, true)); return; }
                events.Report(new(app.Id, "Chờ tải"));
                var path = await DownloadAsync(app, workDir, events, log, token);
                events.Report(new(app.Id, "Chờ cài đặt", 100));
                token.ThrowIfCancellationRequested();
                var isMsi = Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                var file = isMsi ? Path.Combine(Environment.SystemDirectory, "msiexec.exe") : path;
                var args = isMsi ? $"/i \"{path}\" {app.Arguments}" : app.Arguments;
                var command = $"$p = Start-Process -FilePath {Quote(file)} " +
                    (string.IsNullOrWhiteSpace(args) ? "" : $"-ArgumentList {Quote(args)} ") +
                    "-Wait -PassThru -ErrorAction Stop; if ($null -eq $p.ExitCode) { throw 'Installer returned no exit code' }; exit $p.ExitCode";
                int code = 1618;
                for (var attempt = 0; attempt <= 3; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (isMsi) await msiInstallSlot.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        events.Report(new(app.Id, string.IsNullOrWhiteSpace(app.Arguments) ? "Đang cài · hãy thao tác trong bộ cài" : "Đang cài đặt", 100));
                        log.Report($"{app.Name}: bắt đầu bộ cài. Chờ toàn bộ tiến trình con kết thúc.");
                        // No forced cancellation once an installer starts: rollback is installer-specific.
                        code = await run(command, workDir, log);
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
                var code = await run(command, workDir, log);
                if (code != 0) throw new InvalidOperationException($"Mã lỗi {code}.");
                events.Report(new(option.TaskId, "Hoàn tất", 100, true));
            }
            catch (OperationCanceledException) { events.Report(new(option.TaskId, "Đã hủy", 0, true)); }
            catch (Exception ex) { log.Report($"Windows/{option.Name}: {ex.Message}"); events.Report(new(option.TaskId, "Thất bại", 0, true, true)); }
        }).ToArray();
        await Task.WhenAll(appTasks.Concat(settingTasks));
    }

    private async Task<string> DownloadAsync(AppDefinition app, string workDir, IProgress<DeploymentEvent> events, IProgress<string> log, CancellationToken token)
    {
        var path = Path.Combine(workDir, app.Id + Path.GetExtension(new Uri(app.Url).AbsolutePath));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                using var response = await http.GetAsync(app.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Chuyển hướng tải không an toàn.");
                using var input = await response.Content.ReadAsStreamAsync();
                using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                using (deadline.Token.Register(() =>
                {
                    // Framework response streams do not consistently observe cancellation while a read is stalled.
                    try { input.Dispose(); } catch { }
                    try { response.Dispose(); } catch { }
                }))
                {
                    var total = response.Content.Headers.ContentLength;
                    var buffer = new byte[81920]; long received = 0;
                    var clock = Stopwatch.StartNew();
                    while (true)
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(90));
                        var size = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token);
                        if (size == 0) break;
                        await output.WriteAsync(buffer, 0, size, deadline.Token);
                        received += size;
                        if (clock.ElapsedMilliseconds < 150) continue;
                        var percent = total > 0 ? received * 100d / total.Value : 0;
                        events.Report(new(app.Id, $"Đang tải · {received / 1048576d:0.0} MB", percent));
                        clock.Restart();
                    }
                    if (total.HasValue && received != total.Value) throw new IOException("File tải chưa đầy đủ.");
                }
                await VerifyFileAsync(path, app.Sha256, token);
                return path;
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            catch (Exception ex) when (attempt < 3)
            {
                log.Report($"{app.Name}: tải lần {attempt} lỗi ({ex.Message}); thử lại cùng URL.");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), token);
            }
            catch (OperationCanceledException ex)
            {
                throw new TimeoutException("Download timed out after three attempts.", ex);
            }
        }
        throw new IOException("Không tải được bộ cài.");
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
        if (app.Id == "evkey" && (File.Exists(@"C:\EVKey\EVKey64.exe") || File.Exists(@"C:\EVKey\EVKey.exe"))) return true;
        if (app.Id == "zalo" && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Zalo", "Zalo.exe"))) return true;
        if (app.Id == "telegram" && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegram Desktop", "Telegram.exe"))) return true;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string display && InstalledNameMatches(app, display)) return true;
            }
        }
        return false;
    }

    internal static bool InstalledNameMatches(AppDefinition app, string displayName)
    {
        var pattern = app.Id.ToLowerInvariant() switch
        {
            "evkey" => @"^EVKey(?:\s|$)",
            "chrome" => @"^Google Chrome(?:\s|$)",
            "klite" => @"K-Lite Codec Pack",
            "telegram" => @"^Telegram Desktop(?:\s|$)",
            "ultraview" => @"^UltraViewer(?:\s|$)",
            "winrar" => @"^WinRAR(?:\s|$)",
            "zalo" => @"^Zalo(?:\s|$)",
            "zoom" => @"^Zoom(?: Workplace)?(?:\s|$)",
            "office" => @"^Microsoft (?:Office|365)(?:\s|$)",
            "wps" => @"^WPS Office(?:\s|$)",
            "vc64" => @"^Microsoft Visual C\+\+.*\bx64\b",
            "vc86" => @"^Microsoft Visual C\+\+.*\bx86\b",
            // Custom entries use a conservative name rule: exact name, a numeric version, or architecture/details in parentheses.
            // This avoids treating related products such as "Zoom Outlook Plugin" as the main application.
            _ => "^" + Regex.Escape(app.Name.Trim()) + @"(?:$|\s+(?:v(?:ersion)?\s*)?\d|\s*\()"
        };
        return app.Name.Trim().Length > 0 && Regex.IsMatch(displayName, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
    }

    internal static async Task<int> RunPowerShellAsync(string command, string workDir, IProgress<string> log)
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
            log.Report("Tác vụ đã chạy hơn 30 phút. Tiếp tục chờ để không làm hỏng cài đặt; chưa được đóng ứng dụng.");
        await exit;
        await Task.WhenAll(output, error);
        return process.ExitCode;
    }
}
