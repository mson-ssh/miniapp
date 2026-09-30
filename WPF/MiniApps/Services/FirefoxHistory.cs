using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MiniApps.Services;

// Firefox places.sqlite contains BOTH history and bookmarks. Never delete that file.
// References: https://firefox-source-docs.mozilla.org/browser/places/History.html
// https://support.mozilla.org/en-US/kb/profiles-where-firefox-stores-user-data
internal static class FirefoxHistory
{
    internal static void Clear(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            if (!File.Exists(candidate)) continue;
            using var file = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (!GetFileInformationByHandle(file.SafeFileHandle, out var info) || info.NumberOfLinks != 1)
                throw new IOException("Không sửa cơ sở dữ liệu Firefox có hard link hoặc không xác minh được liên kết.");
        }
        using var db = new CleanSqlite(path);
        db.Execute("PRAGMA busy_timeout=0; PRAGMA secure_delete=ON; BEGIN EXCLUSIVE;");
        try
        {
            if (db.Scalar("PRAGMA quick_check") != "ok") throw new IOException("Cơ sở dữ liệu Firefox không hợp lệ; giữ nguyên.");
            // Unknown persistent triggers may require browser-only SQL functions. Fail closed.
            if (db.Scalar("SELECT count(*) FROM sqlite_master WHERE type='trigger'") != "0")
                throw new IOException("Schema Firefox có trigger chưa hỗ trợ; giữ nguyên lịch sử và dấu trang.");
            var bookmarkDigest = Digest(db.Query("SELECT * FROM moz_bookmarks ORDER BY id"));
            var tables = new HashSet<string>(db.Query("SELECT name FROM sqlite_master WHERE type='table'").Select(row => row[0]!), StringComparer.Ordinal);
            var columns = new HashSet<string>(db.Query("PRAGMA table_info(moz_places)").Select(row => row[1]!), StringComparer.Ordinal);
            foreach (var column in new[] { "id", "foreign_count", "visit_count", "last_visit_date", "frecency" })
                if (!columns.Contains(column)) throw new IOException("Schema Firefox chưa được hỗ trợ; giữ nguyên dữ liệu.");
            // Delete visits and history-derived metadata, not bookmarks, credentials or site data.
            foreach (var table in new[] { "moz_places_metadata", "moz_places_metadata_search_queries", "moz_inputhistory" })
                if (tables.Contains(table)) db.Execute("DELETE FROM " + table);
            db.Execute("DELETE FROM moz_historyvisits;");
            db.Execute("DELETE FROM moz_places WHERE foreign_count=0 AND NOT EXISTS (SELECT 1 FROM moz_bookmarks WHERE fk=moz_places.id);");
            var updates = new List<string> { "visit_count=0", "last_visit_date=NULL", "frecency=0" };
            foreach (var name in new[] { "typed", "alt_frecency" }) if (columns.Contains(name)) updates.Add(name + "=0");
            foreach (var name in new[] { "recalc_frecency", "recalc_alt_frecency" }) if (columns.Contains(name)) updates.Add(name + "=1");
            db.Execute("UPDATE moz_places SET " + string.Join(",", updates));
            if (tables.Contains("moz_historyvisits_extra")) db.Execute("DELETE FROM moz_historyvisits_extra");
            if (tables.Contains("moz_places_extra")) db.Execute("DELETE FROM moz_places_extra WHERE NOT EXISTS (SELECT 1 FROM moz_places WHERE id=place_id)");
            if (tables.Contains("moz_annos")) db.Execute("DELETE FROM moz_annos WHERE NOT EXISTS (SELECT 1 FROM moz_places WHERE id=place_id)");
            if (tables.Contains("moz_origins") && columns.Contains("origin_id"))
                db.Execute("DELETE FROM moz_origins WHERE NOT EXISTS (SELECT 1 FROM moz_places WHERE origin_id=moz_origins.id)");
            if (bookmarkDigest != Digest(db.Query("SELECT * FROM moz_bookmarks ORDER BY id")) ||
                db.Scalar("SELECT count(*) FROM moz_historyvisits") != "0" || db.Scalar("PRAGMA quick_check") != "ok")
                throw new IOException("Kiểm tra sau dọn không đạt; hoàn tác giao dịch Firefox.");
            db.Execute("COMMIT;");
        }
        catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
        // This is browsing-history cleanup, not a forensic secure-erasure guarantee. The commit above is
        // final; a checkpoint that cannot run now is done by SQLite/Firefox later, so it is not a failure.
        try { db.Execute("PRAGMA wal_checkpoint(TRUNCATE);"); }
        catch (IOException) { }
    }
    private static string Digest(IEnumerable<string?[]> rows)
    {
        using var sha = SHA256.Create();
        var text = new StringBuilder();
        foreach (var row in rows) foreach (var value in row) text.Append(value?.Length ?? -1).Append(':').Append(value).Append(';');
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out FileInformation info);
}

// Windows 10/11's SQLite API: no download, no external executable, UTF-8 paths.
internal sealed class CleanSqlite : IDisposable
{
    private IntPtr handle;
    internal CleanSqlite(string path, bool create = false)
    {
        var result = sqlite3_open_v2(Utf8(path), out handle, 2 | (create ? 4 : 0), IntPtr.Zero);
        if (result != 0) { Dispose(); throw new IOException("Không mở được SQLite (mã " + result + ")."); }
    }
    internal void Execute(string sql)
    {
        var result = sqlite3_exec(handle, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) sqlite3_free(error);
        if (result != 0) throw new IOException("SQLite từ chối thao tác (mã " + result + "); dữ liệu đang dùng hoặc schema chưa hỗ trợ.");
    }
    internal List<string?[]> Query(string sql)
    {
        var result = sqlite3_prepare_v2(handle, Utf8(sql), -1, out var statement, IntPtr.Zero);
        if (result != 0) throw new IOException("Không đọc được schema SQLite (mã " + result + ").");
        try
        {
            var rows = new List<string?[]>();
            while ((result = sqlite3_step(statement)) == 100)
            {
                var row = new string?[sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++)
                {
                    var pointer = sqlite3_column_text(statement, i);
                    if (pointer == IntPtr.Zero) continue;
                    var bytes = new byte[sqlite3_column_bytes(statement, i)];
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    row[i] = Encoding.UTF8.GetString(bytes);
                }
                rows.Add(row);
            }
            if (result != 101) throw new IOException("SQLite đang bị khóa hoặc không đọc được (mã " + result + ").");
            return rows;
        }
        finally { sqlite3_finalize(statement); }
    }
    internal string? Scalar(string sql) => Query(sql).FirstOrDefault()?.FirstOrDefault();
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");
    public void Dispose() { if (handle != IntPtr.Zero) { sqlite3_close(handle); handle = IntPtr.Zero; } }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr error);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr pointer);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr statement);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);
}
