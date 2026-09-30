using System.Diagnostics;
using System.IO;
using MiniApps.Services;

internal static class CleanTests
{
    internal static void Run(string root, Action<string, Action> check)
    {
        var log = new Sink();
        CleanScope Scope(string name)
        {
            var user = Path.Combine(root, name);
            var local = Path.Combine(user, "AppData", "Local");
            var roaming = Path.Combine(user, "AppData", "Roaming");
            var temp = Path.Combine(local, "Temp");
            Directory.CreateDirectory(temp); Directory.CreateDirectory(roaming);
            return new(user, local, roaming, temp, [Path.Combine(temp, "active-app")]);
        }
        string Put(string parent, string relative, string text = "fixture")
        {
            var path = Path.Combine(parent, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
        }
        void Assert(bool value) { if (!value) throw new Exception("CLEAN fixture assertion failed"); }
        void Reject(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("CLEAN must refuse this operation"); }

        check("CLEAN removes only temp and allowlisted Chromium history/cache for the current account", () =>
        {
            var scope = Scope("clean-current");
            var junk = Put(scope.Temp, @"installer\junk.tmp");
            var protectedFiles = new[] { Put(scope.Temp, @"MiniApps\work-fixture\keep"), Put(scope.Temp, @"old\Backups\registry.reg"),
                Put(scope.Temp, @"active-app\MiniApps.exe"), Put(scope.Temp, "backup.bak"),
                Put(Path.Combine(root, "other-account"), @"AppData\Local\Temp\keep.tmp"), Put(scope.UserRoot, @"Downloads\setup.exe") };
            var chrome = Path.Combine(scope.Local, @"Google\Chrome\User Data");
            var delete = new List<string>(); var keep = new List<string>();
            foreach (var profileName in new[] { "Default", "Profile 1" })
            {
                var profile = Path.Combine(chrome, profileName);
                keep.Add(Put(profile, "Preferences", "{}"));
                foreach (var file in new[] { "History", "History-wal", "History-shm", @"Cache\Cache_Data\data_0", @"Code Cache\js\cache", @"Service Worker\CacheStorage\one", "Top Sites", "Shortcuts", "Favicons" }) delete.Add(Put(profile, file));
                foreach (var file in new[] { "Bookmarks", "Login Data", @"Network\Cookies", @"Local Storage\data", @"IndexedDB\data", @"Sessions\Session_1", "Web Data" }) keep.Add(Put(profile, file));
            }
            var unsupported = Put(scope.Local, @"UnknownBrowser\User Data\Default\History");
            var result = new CleanEngine(scope, log).Run();
            Assert(!File.Exists(junk) && delete.All(path => !File.Exists(path)) && keep.Concat(protectedFiles).All(File.Exists));
            Assert(File.Exists(unsupported) && Directory.Exists(scope.Temp) && result.DeletedFiles == delete.Count + 1 && result.Skipped == 0);
            Assert(!File.Exists(Path.Combine(chrome, "lockfile")));
        });
        check("CLEAN blocks all deletion when TEMP is redirected/broad", () =>
        {
            var scope = Scope("clean-busy");
            var sentinel = Put(scope.Temp, "sentinel.tmp");
            var closed = 0;
            Reject(() => new CleanEngine(scope with { Temp = scope.UserRoot }, log, () => closed++).Run());
            Reject(() => new CleanEngine(scope with { Temp = Path.Combine(root, "other-account", "Temp") }, log, () => closed++).Run());
            Reject(() => new CleanEngine(scope with { SystemTemp = scope.UserRoot }, log, () => closed++).Run());
            Reject(() => new CleanEngine(scope with { SystemTemp = Path.GetPathRoot(root)! + "Temp" }, log, () => closed++).Run());
            // Browsers are never closed for a run that is refused.
            Assert(File.Exists(sentinel) && closed == 0);
        });
        check("CLEAN uses the account's Local\\Temp when bootstrap redirects %TEMP% to its session", () =>
        {
            // Only reads the scope; nothing is cleaned. Mirrors bootstrap: TEMP=<Temp>\MiniApps\<session>\temp.
            var old = (Temp: Environment.GetEnvironmentVariable("TEMP"), Tmp: Environment.GetEnvironmentVariable("TMP"), Session: Environment.GetEnvironmentVariable("MINIAPPS_SESSION"));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var session = Path.Combine(local, @"Temp\MiniApps\session-fixture");
            try
            {
                Environment.SetEnvironmentVariable("TEMP", Path.Combine(session, "temp")); Environment.SetEnvironmentVariable("TMP", Path.Combine(session, "temp"));
                Environment.SetEnvironmentVariable("MINIAPPS_SESSION", session);
                var scope = CleanService.CurrentScope();
                Assert(CleanEngine.Full(scope.Temp).Equals(CleanEngine.Full(Path.Combine(scope.Local, "Temp")), StringComparison.OrdinalIgnoreCase));
                Assert(CleanEngine.Under(scope.Temp, scope.UserRoot) && scope.ProtectedPaths.Contains(session));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEMP", old.Temp); Environment.SetEnvironmentVariable("TMP", old.Tmp); Environment.SetEnvironmentVariable("MINIAPPS_SESSION", old.Session);
            }
        });
        check("CLEAN keeps the running bootstrap session inside TEMP", () =>
        {
            var scope = Scope("clean-bootstrap");
            var session = Path.Combine(scope.Temp, @"MiniApps\session-1");
            var keep = new[] { Put(session, @"app\MiniApps.exe"), Put(session, @"temp\setup.tmp") };
            var junk = Put(scope.Temp, "old-installer.tmp");
            var result = new CleanEngine(scope with { ProtectedPaths = [Path.Combine(session, "app"), session] }, log).Run();
            Assert(keep.All(File.Exists) && !File.Exists(junk) && result.Skipped == 0);
        });
        check("CLEAN removes read-only and long-path TEMP files and Windows Temp, closing browsers first", () =>
        {
            var scope = Scope("clean-readonly");
            var systemTemp = Path.Combine(root, @"clean-readonly-windows\Windows\Temp");
            var readOnly = Put(scope.Temp, @"setup\payload.msi"); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var folder = Path.Combine(scope.Temp, "setup"); new DirectoryInfo(folder).Attributes |= FileAttributes.ReadOnly;
            var deep = scope.Temp;
            for (var i = 0; i < 12; i++) deep = Path.Combine(deep, new string((char)('a' + i), 24));
            var longFile = Put(@"\\?\" + deep, "long.tmp");
            var windowsJunk = Put(systemTemp, @"svc\log.tmp"); var held = Put(systemTemp, "held.tmp");
            var closed = 0;
            using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var result = new CleanEngine(scope with { SystemTemp = systemTemp }, log, () => { closed++; Assert(File.Exists(readOnly)); }).Run();
                Assert(longFile.Length > 260 && closed == 1 && result.Skipped == 0 && result.InUse == 1);
            }
            Assert(!File.Exists(readOnly) && !Directory.Exists(folder) && !File.Exists(longFile) && !File.Exists(windowsJunk));
            Assert(File.Exists(held) && Directory.Exists(systemTemp) && Directory.Exists(scope.Temp));
        });
        check("CLEAN clears Windows Recent items and Explorer lists but keeps Quick access pins", () =>
        {
            var scope = Scope("clean-recent");
            var recent = Path.Combine(scope.Roaming, @"Microsoft\Windows\Recent");
            var delete = new[] { Put(recent, "report.docx.lnk"), Put(recent, @"AutomaticDestinations\5f7b5f1e01b83767.automaticDestinations-ms"),
                Put(recent, @"CustomDestinations\28c8b86deab549a1.customDestinations-ms") };
            var keep = new[] { Put(recent, "desktop.ini"), Put(recent, @"AutomaticDestinations\f01b4d95cf55d32a.automaticDestinations-ms"), Put(scope.Roaming, @"Microsoft\Windows\Start Menu\app.lnk") };
            // A fixture key under HKCU\Software only, never Explorer's real one.
            const string key = @"Software\MiniAppsCleanTest\Explorer";
            using (var explorer = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key))
            {
                using (var docs = explorer.CreateSubKey(@"RecentDocs\.docx")) docs.SetValue("0", new byte[] { 1 });
                using (var run = explorer.CreateSubKey("RunMRU")) { run.SetValue("a", "cmd\\1"); run.SetValue("MRUList", "a"); }
                using (var dialog = explorer.CreateSubKey(@"ComDlg32\OpenSavePidlMRU\*")) dialog.SetValue("0", new byte[] { 1 });
                using (var other = explorer.CreateSubKey("Advanced")) other.SetValue("Hidden", 1);
            }
            try
            {
                var result = new CleanEngine(scope with { ExplorerKey = key }, log).Run();
                Assert(result.Skipped == 0 && result.RecentLists == 3 && delete.All(path => !File.Exists(path)) && keep.All(File.Exists));
                using var explorer = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)!;
                using var docs = explorer.OpenSubKey("RecentDocs")!; using var run = explorer.OpenSubKey("RunMRU")!;
                using var dialog = explorer.OpenSubKey(@"ComDlg32\OpenSavePidlMRU")!; using var other = explorer.OpenSubKey("Advanced")!;
                Assert(docs.SubKeyCount == 0 && run.ValueCount == 0 && dialog.SubKeyCount == 0 && (int)other.GetValue("Hidden")! == 1);
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\MiniAppsCleanTest", false); }
        });
        check("CLEAN skips only the browser holding its profile lock (e.g. Edge Startup boost) and cleans the rest", () =>
        {
            var scope = Scope("clean-open-browser");
            var junk = Put(scope.Temp, "junk.tmp");
            var edge = Path.Combine(scope.Local, @"Microsoft\Edge\User Data");
            Put(edge, @"Default\Preferences"); var edgeHistory = Put(edge, @"Default\History"); var edgeCache = Put(edge, @"Default\Cache\data_0");
            var chrome = Path.Combine(scope.Local, @"Google\Chrome\User Data");
            Put(chrome, @"Default\Preferences"); var chromeHistory = Put(chrome, @"Default\History");
            // Chromium's own lock: write access, read sharing, delete on close.
            using (new FileStream(Path.Combine(edge, "lockfile"), FileMode.Create, FileAccess.Write, FileShare.Read, 1, FileOptions.DeleteOnClose))
            {
                var result = new CleanEngine(scope, log).Run();
                Assert(File.Exists(edgeHistory) && File.Exists(edgeCache) && result.Skipped == 1);
                Assert(!File.Exists(chromeHistory) && !File.Exists(junk));
            }
        });
        check("CLEAN recognizes Cốc Cốc, Brave, Vivaldi, Chromium and old/new Opera layouts", () =>
        {
            var scope = Scope("clean-browser-layouts");
            var history = new List<string>(); var bookmarks = new List<string>();
            foreach (var relative in new[] { @"CocCoc\Browser\User Data", @"BraveSoftware\Brave-Browser\User Data", @"Vivaldi\User Data", @"Chromium\User Data" })
            {
                var profile = Path.Combine(scope.Local, relative, "Default");
                Put(profile, "Preferences"); history.Add(Put(profile, "History")); bookmarks.Add(Put(profile, "Bookmarks"));
            }
            foreach (var relative in new[] { @"Opera Software\Opera Stable", @"Opera Software\Opera GX Stable\Default" })
            {
                var profile = Path.Combine(scope.Roaming, relative);
                Put(profile, "Preferences"); history.Add(Put(profile, "History")); bookmarks.Add(Put(profile, "Bookmarks"));
                history.Add(Put(Path.Combine(scope.Local, relative), @"Cache\cache-file"));
            }
            var result = new CleanEngine(scope, log).Run();
            Assert(result.Skipped == 0 && history.All(path => !File.Exists(path)) && bookmarks.All(File.Exists));
            var second = new CleanEngine(scope, log).Run(); Assert(second.DeletedFiles == 0 && second.Skipped == 0);
        });
        check("CLEAN reports corrupt Firefox history without removing the database or credentials", () =>
        {
            var scope = Scope("clean-corrupt");
            var profile = Path.Combine(scope.Roaming, @"Mozilla\Firefox\Profiles\broken.default");
            var db = Put(profile, "places.sqlite", "not a database"); var login = Put(profile, "logins.json");
            var result = new CleanEngine(scope, log).Run();
            Assert(result.Skipped > 0 && result.HistoryDatabases == 0 && File.ReadAllText(db) == "not a database" && File.Exists(login));
        });
        check("CLEAN keeps locked files and the complete locked Chromium history database family", () =>
        {
            var scope = Scope("clean-locked");
            var tempFile = Put(scope.Temp, "held.tmp");
            var profile = Path.Combine(scope.Local, @"Microsoft\Edge\User Data\Default");
            Put(profile, "Preferences"); var history = Put(profile, "History"); var wal = Put(profile, "History-wal");
            using var tempLock = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var historyLock = new FileStream(history, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = new CleanEngine(scope, log).Run();
            // A held TEMP file is normal (InUse); the held history database is a real skip.
            Assert(result.InUse == 1 && result.Skipped >= 1 && File.Exists(tempFile) && File.Exists(history) && File.Exists(wal));
        });
        check("CLEAN never follows a junction from TEMP", () =>
        {
            var scope = Scope("clean-junction");
            var target = Path.Combine(root, "junction-target"); var sentinel = Put(target, "keep.txt");
            var link = Path.Combine(scope.Temp, "linked");
            var command = "New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null";
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command))) { UseShellExecute = false, CreateNoWindow = true })!;
            process.WaitForExit(); Assert(process.ExitCode == 0);
            try
            {
                var result = new CleanEngine(scope, log).Run();
                Assert(File.Exists(sentinel) && Directory.Exists(link) && result.Skipped > 0);
            }
            finally { Directory.Delete(link, false); } // remove only the fixture junction, not its target.
        });
        check("CLEAN clears Firefox visits while retaining bookmarks, cookies and passwords", () =>
        {
            var scope = Scope("clean-firefox");
            var profile = Path.Combine(scope.Roaming, @"Mozilla\Firefox\Profiles\fixture.default");
            Directory.CreateDirectory(profile);
            var path = Path.Combine(profile, "places.sqlite"); CreateFirefox(path);
            using (var setup = new CleanSqlite(path))
                setup.Execute("CREATE TABLE moz_places_extra(place_id INTEGER PRIMARY KEY, sync_json TEXT); CREATE TABLE moz_historyvisits_extra(visit_id INTEGER PRIMARY KEY, sync_json TEXT);" +
                    "INSERT INTO moz_places_extra VALUES(1,'keep'),(2,'orphan'); INSERT INTO moz_historyvisits_extra VALUES(1,'a'),(2,'b');");
            var favicons = Put(profile, "favicons.sqlite");
            var keep = new[] { Put(profile, "cookies.sqlite"), Put(profile, "logins.json"), Put(profile, "key4.db"), Put(profile, @"bookmarkbackups\bookmarks.jsonlz4"), Put(profile, "sessionstore.jsonlz4") };
            var cache = Put(Path.Combine(scope.Local, @"Mozilla\Firefox\Profiles\fixture.default"), @"cache2\entries\one");
            var result = new CleanEngine(scope, log).Run();
            using var db = new CleanSqlite(path);
            Assert(result.HistoryDatabases == 1 && result.Skipped == 0 && !File.Exists(cache) && !File.Exists(favicons) && keep.All(File.Exists));
            Assert(!File.Exists(Path.Combine(profile, "parent.lock")));
            Assert(db.Scalar("SELECT group_concat(place_id) FROM moz_places_extra") == "1" && db.Scalar("SELECT count(*) FROM moz_historyvisits_extra") == "0");
            Assert(db.Scalar("SELECT count(*) FROM moz_historyvisits") == "0" && db.Scalar("SELECT count(*) FROM moz_places") == "1");
            Assert(db.Scalar("SELECT title FROM moz_bookmarks WHERE id=1") == "keep bookmark" && db.Scalar("SELECT visit_count FROM moz_places") == "0");
            Assert(db.Scalar("PRAGMA quick_check") == "ok");
        });
        check("CLEAN rejects unknown Firefox schema and locked profiles without losing data", () =>
        {
            var scope = Scope("clean-firefox-unknown");
            var profile = Path.Combine(scope.Roaming, @"Mozilla\Firefox\Profiles\fixture.default");
            Directory.CreateDirectory(profile); var path = Path.Combine(profile, "places.sqlite"); CreateFirefox(path);
            using (var db = new CleanSqlite(path)) db.Execute("CREATE TRIGGER unexpected AFTER DELETE ON moz_historyvisits BEGIN SELECT 1; END;");
            var result = new CleanEngine(scope, log).Run();
            using (var db = new CleanSqlite(path)) Assert(result.Skipped > 0 && result.HistoryDatabases == 0 && db.Scalar("SELECT count(*) FROM moz_historyvisits") == "2");
            using var profileLock = new FileStream(Path.Combine(profile, "parent.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            result = new CleanEngine(scope, log).Run(); Assert(result.HistoryDatabases == 0 && result.Skipped > 0);
        });
        check("CLEAN bounds Firefox registered paths to the current account", () =>
        {
            var scope = Scope("clean-firefox-path");
            var other = Path.Combine(root, "other-firefox"); Directory.CreateDirectory(other);
            var path = Path.Combine(other, "places.sqlite"); CreateFirefox(path);
            Put(scope.Roaming, @"Mozilla\Firefox\profiles.ini", "[Profile0]\nIsRelative=0\nPath=" + other);
            var result = new CleanEngine(scope, log).Run();
            using var db = new CleanSqlite(path);
            Assert(result.Skipped > 0 && db.Scalar("SELECT count(*) FROM moz_historyvisits") == "2");
        });
        check("CLEAN rolls back Firefox changes when later schema validation fails", () =>
        {
            var scope = Scope("clean-firefox-rollback"); var path = Put(scope.Temp, "fixture.sqlite", "");
            CreateFirefox(path);
            using (var db = new CleanSqlite(path)) db.Execute("CREATE TABLE moz_origins(wrong_column INTEGER); ALTER TABLE moz_places ADD COLUMN origin_id INTEGER;");
            Reject(() => FirefoxHistory.Clear(path));
            using var after = new CleanSqlite(path);
            Assert(after.Scalar("SELECT count(*) FROM moz_historyvisits") == "2" && after.Scalar("SELECT count(*) FROM moz_places") == "2");
        });
        check("CLEAN refuses hard-linked Firefox databases", () =>
        {
            var scope = Scope("clean-hardlink");
            var original = Path.Combine(scope.Temp, "original.sqlite"); CreateFirefox(original);
            var link = Path.Combine(scope.Temp, "linked.sqlite");
            var command = "New-Item -ItemType HardLink -Path '" + link.Replace("'", "''") + "' -Target '" + original.Replace("'", "''") + "' | Out-Null";
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command))) { UseShellExecute = false, CreateNoWindow = true })!;
            process.WaitForExit(); Assert(process.ExitCode == 0);
            Reject(() => FirefoxHistory.Clear(link));
            using var db = new CleanSqlite(original);
            Assert(db.Scalar("SELECT count(*) FROM moz_historyvisits") == "2");
        });
    }
    private static void CreateFirefox(string path)
    {
        using var db = new CleanSqlite(path, create: true);
        db.Execute("CREATE TABLE moz_places(id INTEGER PRIMARY KEY, url TEXT, foreign_count INTEGER, visit_count INTEGER, last_visit_date INTEGER, frecency INTEGER);" +
            "CREATE TABLE moz_historyvisits(id INTEGER PRIMARY KEY, place_id INTEGER);" +
            "CREATE TABLE moz_bookmarks(id INTEGER PRIMARY KEY, fk INTEGER, title TEXT);" +
            "INSERT INTO moz_places VALUES(1,'https://bookmark.invalid',1,5,123,10),(2,'https://history.invalid',0,2,456,20);" +
            "INSERT INTO moz_historyvisits VALUES(1,1),(2,2); INSERT INTO moz_bookmarks VALUES(1,1,'keep bookmark');");
    }
    private sealed class Sink : IProgress<string> { public void Report(string value) { } }
}
