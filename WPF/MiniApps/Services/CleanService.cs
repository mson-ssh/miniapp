using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace MiniApps.Services;

// SystemTemp (%SystemRoot%\Temp) and ExplorerKey (HKCU subkey holding Explorer's recent lists) are
// optional: empty skips that part, so tests never touch the machine's own folders or registry.
internal sealed record CleanScope(string UserRoot, string Local, string Roaming, string Temp,
    IReadOnlyList<string> ProtectedPaths, string SystemTemp = "", string ExplorerKey = "");
// Skipped: not cleaned and worth a look (browser open, link, unknown schema). InUse: temp/recent files
// held by a running program or not accessible, which is normal and does not make the run incomplete.
internal sealed record CleanResult(int DeletedFiles, long Bytes, int Skipped, int HistoryDatabases, int InUse = 0, int RecentLists = 0);

internal static class CleanService
{
    // An over-the-shoulder UAC login must not silently clean the administrator's profile.
    private static void CheckInteractiveIdentity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var session = Process.GetCurrentProcess().SessionId;
        var found = false;
        foreach (var explorer in Process.GetProcessesByName("explorer"))
        {
            using (explorer)
            {
                if (explorer.SessionId != session) continue;
                IntPtr handle;
                try { handle = explorer.Handle; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { continue; }
                if (!OpenProcessToken(handle, 8, out var token))
                    throw new IOException("Không xác minh được tài khoản Windows đang đăng nhập.");
                try
                {
                    using var shellIdentity = new WindowsIdentity(token);
                    if (shellIdentity.User != identity.User)
                        throw new IOException("MiniApps đang chạy bằng tài khoản khác. Hãy chạy bằng tài khoản Windows cần dọn.");
                    found = true;
                }
                finally { CloseHandle(token); }
            }
        }
        if (!found) throw new IOException("Chưa xác minh được phiên đăng nhập Windows; CLEAN chưa xóa gì.");
    }

    // Not Path.GetTempPath(): bootstrap points %TEMP% at its own session folder, and TEMP may be an
    // 8.3 short path. The account's real temp folder is always Local\Temp; the bootstrap session
    // inside it stays protected through MINIAPPS_SESSION and the MiniApps folder rule.
    internal static CleanScope CurrentScope()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), local,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine(local, "Temp"),
            [AppContext.BaseDirectory, Environment.GetEnvironmentVariable("MINIAPPS_SESSION") ?? ""],
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            @"Software\Microsoft\Windows\CurrentVersion\Explorer");
    }

    internal static Task<ExtensionRunResult> RunAsync(IProgress<string> progress) => Task.Run(() =>
    {
        Directory.CreateDirectory(ExtensionService.LogDirectory);
        var path = Path.Combine(ExtensionService.LogDirectory, $"clean-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        var log = new CleanProgress(line => { writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}"); progress.Report(line); });
        try
        {
            CheckInteractiveIdentity();
            var result = new CleanEngine(CurrentScope(), log, () => BrowserCloser.CloseAll(log)).Run();
            log.Report($"{(result.Skipped == 0 ? "Xong" : "Chưa dọn hết")}: {result.DeletedFiles} file · {result.Bytes / 1048576d:0.0} MB · {result.RecentLists} danh sách Recent · {result.HistoryDatabases} lịch sử Firefox · {result.InUse} file đang được dùng · giữ lại/bỏ qua {result.Skipped} mục.");
            return new ExtensionRunResult(result.Skipped == 0 ? 0 : 2, path);
        }
        catch (Exception ex)
        {
            log.Report("CLEAN dừng: " + ex.Message);
            return new ExtensionRunResult(2, path);
        }
    });

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    private sealed class CleanProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
}

// The browser keeps its history databases open, so CLEAN closes the supported browsers of this
// Windows session first (the confirmation says so). Windows get a normal close; what is left after
// the grace period (background/Startup boost, a "close tabs?" prompt) is ended.
internal static class BrowserCloser
{
    // "browser" is Cốc Cốc's process name but too generic on its own, so it also needs the install path.
    private static readonly (string Name, string PathPart)[] Browsers =
    [
        ("chrome", ""), ("msedge", ""), ("brave", ""), ("vivaldi", ""), ("opera", ""), ("firefox", ""), ("browser", @"\CocCoc\")
    ];

    internal static void CloseAll(IProgress<string> log)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var targets = new List<Process>();
        foreach (var (name, pathPart) in Browsers)
            foreach (var process in Process.GetProcessesByName(name))
            {
                if (process.SessionId == session && (pathPart.Length == 0 || ImagePath(process).IndexOf(pathPart, StringComparison.OrdinalIgnoreCase) >= 0))
                    targets.Add(process);
                else process.Dispose();
            }
        if (targets.Count == 0) return;
        try
        {
            log.Report("CLEAN: đóng trình duyệt đang mở (" + string.Join(", ", targets.Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase)) + ")…");
            // One CloseMainWindow closes one window, so repeat until no window is left or time runs out.
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                var windows = 0;
                foreach (var process in targets)
                {
                    try
                    {
                        process.Refresh();
                        if (process.HasExited || process.MainWindowHandle == IntPtr.Zero) continue;
                        windows++;
                        process.CloseMainWindow();
                    }
                    catch (InvalidOperationException) { }
                }
                if (windows == 0) break;
                Thread.Sleep(500);
            }
            foreach (var process in targets)
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            var left = targets.Where(process =>
            {
                try { return !process.WaitForExit(5000); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
            }).Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (left.Count > 0) log.Report("  Chưa đóng được: " + string.Join(", ", left) + "; trình duyệt này sẽ được bỏ qua.");
        }
        finally { foreach (var process in targets) process.Dispose(); }
    }

    private static string ImagePath(Process process)
    {
        var handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == IntPtr.Zero) return "";
        try
        {
            var text = new StringBuilder(1024);
            var size = text.Capacity;
            return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString() : "";
        }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder text, ref int size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}

// All destructive operations accept only resolved, bounded paths. Never delete a profile or
// TEMP root, never follow junctions/symlinks, and never recurse with Directory.Delete(true).
internal sealed class CleanEngine
{
    private readonly CleanScope scope;
    private readonly IProgress<string> log;
    private readonly Action? closeBrowsers;
    private int deleted, skipped, histories, inUse, recentLists;
    private long bytes;
    private readonly HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
    internal CleanEngine(CleanScope scope, IProgress<string> log, Action? closeBrowsers = null)
    {
        this.scope = scope; this.log = log; this.closeBrowsers = closeBrowsers;
    }
    internal static string Full(string path) =>
        Path.GetFullPath(path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    // .NET Framework does not add the long-path prefix itself; TEMP often holds paths over MAX_PATH.
    // Scope checks always use Full(); only the file system calls get the prefixed form.
    private static string Io(string path) { var full = Full(path); return full.Length >= 240 ? @"\\?\" + full : full; }
    private static string[] Entries(string path) => Directory.GetFileSystemEntries(Io(path)).Select(Full).ToArray();
    internal static bool Under(string path, string root) => Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static void SafePath(string path, string root, bool allowRoot = false)
    {
        var full = Full(path);
        if (!Under(full, root) && !(allowRoot && full.Equals(Full(root), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Đường dẫn nằm ngoài phạm vi đã cho phép.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // GetAttributes, not Exists: broken links must not look like missing files.
            try
            {
                if ((File.GetAttributes(Io(current)) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Giữ nguyên đường dẫn liên kết/junction.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    // Sharing/lock violation: another program has the file open.
    private static bool InUse(Exception ex) => (ex.HResult & 0xFFFF) is 32 or 33;
    // Chromium holds <User Data>\lockfile (delete-on-close, no write sharing) for as long as any of its
    // processes run, background/Startup boost included. Holding it ourselves keeps the browser from
    // starting while its profile is cleaned. Nothing is force-closed.
    private static FileStream LockChromium(string root)
    {
        var path = Path.Combine(root, "lockfile");
        SafePath(path, root);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException ex) when (InUse(ex))
        {
            throw new IOException("Trình duyệt đang mở (kể cả chạy nền); bỏ qua trình duyệt này. Đóng hẳn trình duyệt rồi chạy CLEAN lại.");
        }
    }
    internal CleanResult Run()
    {
        // Refuse a redirected/broad TEMP, or a profile from another account, before any deletion.
        if (string.IsNullOrWhiteSpace(scope.UserRoot) || Full(scope.UserRoot).Length <= 3)
            throw new IOException("Không xác định được thư mục tài khoản Windows.");
        foreach (var root in new[] { scope.Local, scope.Roaming, scope.Temp }) SafePath(root, scope.UserRoot);
        if (!Full(scope.Temp).Equals(Full(Path.Combine(scope.Local, "Temp")), StringComparison.OrdinalIgnoreCase))
            throw new IOException("%TEMP% đã chuyển khỏi AppData\\Local\\Temp; CLEAN chưa hỗ trợ đường dẫn này và chưa xóa gì.");
        if (new[] { scope.UserRoot, scope.Local, scope.Roaming }.Any(root => Full(root).Equals(Full(scope.Temp), StringComparison.OrdinalIgnoreCase)) ||
            Under(scope.Local, scope.Temp) || Under(scope.Roaming, scope.Temp))
            throw new IOException("%TEMP% quá rộng; CLEAN từ chối dọn để bảo vệ dữ liệu.");
        var systemTemp = scope.SystemTemp.Length == 0 ? "" : Full(scope.SystemTemp);
        if (systemTemp.Length > 0 && (!Path.GetFileName(systemTemp).Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
            Path.GetDirectoryName(Path.GetDirectoryName(systemTemp)) == null || Under(scope.UserRoot, systemTemp) ||
            Full(scope.UserRoot).Equals(systemTemp, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Thư mục Temp của Windows không hợp lệ; CLEAN chưa xóa gì.");
        // Browsers are closed only once the scope is known to be valid, and before TEMP so their
        // temporary files are released as well.
        closeBrowsers?.Invoke();
        log.Report("CLEAN: dọn file tạm của tài khoản hiện tại…");
        if (Directory.Exists(scope.Temp)) foreach (var child in Children(scope.Temp)) Remove(child, scope.Temp, true);
        if (systemTemp.Length > 0 && Directory.Exists(systemTemp))
        {
            log.Report("CLEAN: dọn file tạm của Windows…");
            foreach (var child in Children(systemTemp, systemTemp)) Remove(child, systemTemp, true);
        }
        Recent();
        var browsers = new (string Name, string Relative)[]
        {
            ("Chrome", @"Google\Chrome\User Data"), ("Chrome Beta", @"Google\Chrome Beta\User Data"),
            ("Chrome Dev", @"Google\Chrome Dev\User Data"), ("Chrome Canary", @"Google\Chrome SxS\User Data"),
            ("Edge", @"Microsoft\Edge\User Data"), ("Edge Beta", @"Microsoft\Edge Beta\User Data"),
            ("Edge Dev", @"Microsoft\Edge Dev\User Data"), ("Edge Canary", @"Microsoft\Edge SxS\User Data"),
            ("Cốc Cốc", @"CocCoc\Browser\User Data"), ("Brave", @"BraveSoftware\Brave-Browser\User Data"),
            ("Brave Beta", @"BraveSoftware\Brave-Browser-Beta\User Data"), ("Brave Nightly", @"BraveSoftware\Brave-Browser-Nightly\User Data"),
            ("Vivaldi", @"Vivaldi\User Data"), ("Chromium", @"Chromium\User Data")
        };
        foreach (var browser in browsers) Chromium(browser.Name, Path.Combine(scope.Local, browser.Relative));
        foreach (var opera in new[] { "Opera Stable", "Opera GX Stable" })
        {
            var local = Path.Combine(scope.Local, "Opera Software", opera);
            Chromium(opera, Path.Combine(scope.Roaming, "Opera Software", opera), local, Path.Combine(local, "Default"));
        }
        Firefox();
        log.Report("  Chỉ xử lý profile chuẩn đã nhận diện; không quét profile portable/vị trí tùy chỉnh của Chromium hoặc dữ liệu đồng bộ trên mạng.");
        return new(deleted, bytes, skipped, histories, inUse, recentLists);
    }
    // Windows' recent items: the Recent shortcuts, app jump lists and Explorer's typed/run/open-save
    // lists. Quick access pins live in f01b4d95cf55d32a.automaticDestinations-ms and are kept.
    private void Recent()
    {
        var recent = Path.Combine(scope.Roaming, @"Microsoft\Windows\Recent");
        if (Directory.Exists(recent))
        {
            log.Report("CLEAN: xóa danh sách Recent của Windows…");
            foreach (var child in Children(recent))
            {
                var name = Path.GetFileName(child);
                if (name.Equals("AutomaticDestinations", StringComparison.OrdinalIgnoreCase) || name.Equals("CustomDestinations", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in Children(child))
                        if (!Path.GetFileName(item).Equals("f01b4d95cf55d32a.automaticDestinations-ms", StringComparison.OrdinalIgnoreCase) && File.Exists(item))
                            Remove(item, recent, true);
                }
                else if (!name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && File.Exists(child)) Remove(child, recent, true);
            }
        }
        if (scope.ExplorerKey.Length == 0) return;
        try
        {
            using var explorer = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(scope.ExplorerKey, true);
            if (explorer == null) return;
            foreach (var name in new[] { "RecentDocs", "RunMRU", "TypedPaths", "WordWheelQuery", @"ComDlg32\OpenSavePidlMRU",
                @"ComDlg32\LastVisitedPidlMRU", @"ComDlg32\CIDSizeMRU", @"ComDlg32\OpenSaveMRU", @"ComDlg32\LastVisitedMRU" })
            {
                try
                {
                    // Empty the list but keep its key, as Explorer's own "Clear" does.
                    using var key = explorer.OpenSubKey(name, true);
                    if (key == null) continue;
                    foreach (var value in key.GetValueNames()) key.DeleteValue(value, false);
                    foreach (var sub in key.GetSubKeyNames()) key.DeleteSubKeyTree(sub, false);
                    recentLists++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Skip(@"HKCU\" + scope.ExplorerKey + "\\" + name, ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Skip(@"HKCU\" + scope.ExplorerKey, ex.Message);
        }
    }
    private string[] Children(string path) => Children(path, scope.UserRoot);
    private string[] Children(string path, string root)
    {
        try { SafePath(path, root, true); return Entries(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skip(path, ex.Message); return []; }
    }
    private void Skip(string path, string reason) { skipped++; log.Report("  Giữ lại " + path + ": " + reason); }
    private bool Protected(string path, bool temp)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("Backups", StringComparison.OrdinalIgnoreCase) || name.Equals("Backup", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("debloat-", StringComparison.OrdinalIgnoreCase)) return true;
        if (!temp) return false;
        if (name.Equals("MiniApps", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return true;
        return scope.ProtectedPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p =>
            Full(p).Equals(Full(path), StringComparison.OrdinalIgnoreCase) || Under(p, path) || Under(path, p));
    }
    private void Remove(string path, string root, bool temp = false)
    {
        try
        {
            SafePath(path, root);
            if (Protected(path, temp)) { log.Report("  Bảo toàn: " + path); return; }
            if (Directory.Exists(Io(path)))
            {
                foreach (var child in Entries(path)) Remove(child, root, temp);
                SafePath(path, root);
                // A skipped/locked/protected child keeps its parents too.
                if (!Directory.EnumerateFileSystemEntries(Io(path)).Any())
                {
                    var folder = new DirectoryInfo(Io(path));
                    if ((folder.Attributes & FileAttributes.ReadOnly) != 0) folder.Attributes &= ~FileAttributes.ReadOnly;
                    Directory.Delete(Io(path), false);
                }
            }
            else if (File.Exists(Io(path)))
            {
                var file = new FileInfo(Io(path));
                var size = file.Length;
                // Installers leave read-only files in TEMP; they are removable junk too. Only the
                // read-only flag of an allowlisted, already bounded path is cleared, never ACLs.
                if (file.IsReadOnly) file.IsReadOnly = false;
                // Test ownership/locks before deleting. Never force-close a process that holds a file.
                using (new FileStream(Io(path), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                SafePath(path, root);
                File.Delete(Io(path)); deleted++; bytes += size;
            }
        }
        // Temp/recent files held by a running program or owned by a service with no access are normal.
        catch (Exception ex) when (temp && (ex is UnauthorizedAccessException || ex is IOException io && InUse(io))) { inUse++; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skip(path, ex.Message); }
    }
    private void CacheFolders(string profile)
    {
        if (!Directory.Exists(profile)) return;
        foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "GrShaderCache", "ShaderCache", @"Service Worker\CacheStorage", @"Service Worker\ScriptCache" })
            Remove(Path.Combine(profile, cache), scope.UserRoot);
    }
    private void Chromium(string name, string root, params string[] localCaches)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            SafePath(root, scope.UserRoot);
            log.Report("CLEAN: " + name + "…");
            using var browserLock = LockChromium(root);
            var profiles = new List<string>();
            if (File.Exists(Path.Combine(root, "Preferences"))) profiles.Add(root); // Opera pre-102.
            foreach (var child in Children(root))
            {
                SafePath(child, root);
                if (Directory.Exists(child) && File.Exists(Path.Combine(child, "Preferences"))) profiles.Add(child);
            }
            if (profiles.Count == 0) Skip(root, "Không nhận diện được profile; không xóa phỏng đoán.");
            foreach (var profile in profiles)
            {
                // Favicons maps visited URLs to icons; bookmark icons are fetched again on the next visit.
                foreach (var database in new[] { "History", "Archived History", "Visited Links", "Top Sites", "Shortcuts", "Network Action Predictor", "Favicons" })
                    RemoveHistoryFamily(Path.Combine(profile, database), root);
                CacheFolders(profile);
            }
            CacheFolders(root);
            foreach (var cache in localCaches) CacheFolders(cache);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skip(root, ex.Message); }
    }
    private void RemoveHistoryFamily(string database, string root)
    {
        // Never delete WAL/journal while its main database is still locked or could not be deleted.
        var files = new[] { database, database + "-journal", database + "-wal", database + "-shm" };
        var handles = new List<FileStream>();
        try
        {
            foreach (var path in files)
            {
                SafePath(path, root);
                if (File.Exists(path)) handles.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skip(database, ex.Message); return; }
        finally { foreach (var handle in handles) handle.Dispose(); }
        Remove(database, root);
        if (File.Exists(database)) return;
        foreach (var path in files.Skip(1)) Remove(path, root);
    }
    private void Firefox()
    {
        var root = Path.Combine(scope.Roaming, "Mozilla", "Firefox");
        if (!Directory.Exists(root)) return;
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            SafePath(root, scope.UserRoot);
            var standard = Path.Combine(root, "Profiles");
            if (Directory.Exists(standard)) foreach (var path in Children(standard)) if (Directory.Exists(path)) profiles.Add(Full(path));
            var ini = Path.Combine(root, "profiles.ini");
            SafePath(ini, root);
            if (File.Exists(ini))
            {
                // Path entries are explicit registrations, not a scan of arbitrary user directories.
                foreach (var line in File.ReadAllLines(ini).Select(line => line.Trim()).Where(line => line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)))
                {
                    var path = line.Substring(5).Replace('/', Path.DirectorySeparatorChar);
                    profiles.Add(Full(Path.IsPathRooted(path) ? path : Path.Combine(root, path)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Skip(root, ex.Message); }
        foreach (var profile in profiles)
        {
            if (!visited.Add(profile)) continue;
            try
            {
                SafePath(profile, scope.UserRoot);
                log.Report("CLEAN: Firefox…");
                var dbPath = Path.Combine(profile, "places.sqlite");
                SafePath(dbPath, profile);
                foreach (var suffix in new[] { "-wal", "-shm", "-journal" }) SafePath(dbPath + suffix, profile);
                var lockPath = Path.Combine(profile, "parent.lock");
                SafePath(lockPath, profile);
                // Firefox holds parent.lock unshared while running; holding it here keeps Firefox from
                // opening this profile until the cleanup is done. Deleted on close, as Firefox does.
                FileStream profileLock;
                try { profileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
                catch (IOException ex) when (InUse(ex)) { throw new IOException("Firefox đang mở; bỏ qua profile này. Đóng hẳn Firefox rồi chạy CLEAN lại."); }
                using (profileLock)
                {
                    if (File.Exists(dbPath))
                    {
                        FirefoxHistory.Clear(dbPath);
                        histories++;
                    }
                    // favicons.sqlite maps visited URLs to icons; Firefox recreates it, bookmark icons return on the next visit.
                    RemoveHistoryFamily(Path.Combine(profile, "favicons.sqlite"), profile);
                    foreach (var cache in new[] { "cache2", "startupCache", "thumbnails" }) Remove(Path.Combine(profile, cache), profile);
                }
                if (Under(profile, root))
                {
                    var relative = Full(profile).Substring(Full(root).Length + 1);
                    var localProfile = Path.Combine(scope.Local, "Mozilla", "Firefox", relative);
                    foreach (var cache in new[] { "cache2", "startupCache", "thumbnails" }) Remove(Path.Combine(localProfile, cache), scope.Local);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or DllNotFoundException or EntryPointNotFoundException) { Skip(profile, ex.Message); }
        }
    }
}
