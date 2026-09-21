using System.Text.RegularExpressions;

namespace MiniApps.Services;

// Removes work folders an earlier run could not delete, for example because an installer
// still held a file when the run ended. Runs at startup, before any install can begin.
public static class WorkFolderCleaner
{
    public static string DefaultRoot => Path.Combine(Path.GetTempPath(), "MiniApps");

    // Returns how many folders were removed. Removes nothing while any MiniApps window is
    // installing, because that window's own work folder lives under the same root.
    public static int RemoveStale(string root)
    {
        using var deploymentLock = new Mutex(false, DeploymentService.DeploymentLockName);
        bool acquired;
        try { acquired = deploymentLock.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return 0;
        try
        {
            if (!Directory.Exists(root)) return 0;
            var removed = 0;
            foreach (var folder in Directory.GetDirectories(root, "work-*"))
            {
                if (!Regex.IsMatch(Path.GetFileName(folder), "^work-[0-9a-f]{32}$")) continue;
                try
                {
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    if (HoldsBackup(folder)) continue;
                    Directory.Delete(folder, true);
                    removed++;
                }
                // Still in use: the next start tries again.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return removed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        finally { deploymentLock.ReleaseMutex(); }
    }

    // Older releases ran Debloat in the same work folder and deliberately left it behind when its
    // registry backup could not be moved out. Those folders are the user's backup, never cleanup.
    // A folder that cannot be fully inspected is kept too.
    private static bool HoldsBackup(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder, "debloat-*").Any() ||
                Directory.EnumerateDirectories(folder, "Backups", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }
}
