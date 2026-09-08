#if NET48
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MiniApps.Models;

namespace MiniApps.Services;

public enum SoftwareDetectionState { Installed, NotInstalled, Unknown }

public sealed record InstalledSoftwareEntry(
    string DisplayName,
    string DisplayVersion,
    string Publisher,
    string InstallLocation,
    string Source);

public sealed record VisualCRuntimeEntry(string Architecture, bool Installed, string Version, string Source);

public sealed class InstalledSoftwareSnapshot
{
    public List<InstalledSoftwareEntry> Entries { get; } = [];
    public List<string> OfficeProductReleaseIds { get; } = [];
    public List<VisualCRuntimeEntry> VisualCRuntimes { get; } = [];
    public Dictionary<string, string> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ReadErrors { get; } = [];
}

public sealed record SoftwareDetectionResult(
    SoftwareDetectionState State,
    string Reason,
    string Evidence = "");

internal static class InstalledSoftwareDetector
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string ClickToRunPath = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";
    private const string VcRuntimePath = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes";

    internal static InstalledSoftwareSnapshot Capture()
    {
        var snapshot = new InstalledSoftwareSnapshot();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            ReadUninstall(snapshot, hive, view);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            ReadOfficeClickToRun(snapshot, view);
            ReadVisualCRuntime(snapshot, view, "x64");
            ReadVisualCRuntime(snapshot, view, "x86");
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        AddKnownExecutable(snapshot, "evkey", Path.Combine(systemRoot, "EVKey", "EVKey64.exe"));
        AddKnownExecutable(snapshot, "evkey", Path.Combine(systemRoot, "EVKey", "EVKey.exe"));
        AddKnownExecutable(snapshot, "zalo", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Zalo", "Zalo.exe"));
        AddKnownExecutable(snapshot, "telegram", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegram Desktop", "Telegram.exe"));
        return snapshot;
    }

    internal static SoftwareDetectionResult Detect(AppDefinition app, InstalledSoftwareSnapshot snapshot)
    {
        var id = app.Id.Trim().ToLowerInvariant();
        if (Catalog.Defaults().FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } builtIn &&
            !builtIn.Name.Equals(app.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(SoftwareDetectionState.Unknown,
                "ID mặc định đang mang tên sản phẩm khác; không dùng rule Smart Skip cũ.", $"{app.Id}: {app.Name}");

        if (id is "vc64" or "vc86")
        {
            var architecture = id == "vc64" ? "x64" : "x86";
            var runtime = snapshot.VisualCRuntimes.FirstOrDefault(item =>
                item.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase) && item.Installed && IsV14(item.Version));
            if (runtime != null)
                return new(SoftwareDetectionState.Installed, $"Visual C++ runtime v14 {architecture} đã đăng ký.",
                    $"{runtime.Source}; Version={runtime.Version}");
        }

        if (id == "office")
        {
            var product = snapshot.OfficeProductReleaseIds.FirstOrDefault(IsOfficeSuiteProductId);
            if (product != null)
                return new(SoftwareDetectionState.Installed, "Đã tìm thấy bộ Office Click-to-Run.", product);
        }

        if (snapshot.Executables.TryGetValue(id, out var executable))
            return new(SoftwareDetectionState.Installed, "Đã tìm thấy executable ở đường cài đặt chuẩn.", executable);

        var match = snapshot.Entries.FirstOrDefault(entry => InstalledNameMatches(app, entry.DisplayName));
        if (match != null)
            return new(SoftwareDetectionState.Installed, "Đã tìm thấy đăng ký ứng dụng.",
                $"{match.DisplayName} {match.DisplayVersion}".Trim() + $"; {match.Source}");

        if (snapshot.ReadErrors.Count > 0)
            return new(SoftwareDetectionState.Unknown,
                "Không đọc được đầy đủ nguồn nhận diện; Smart Skip không thể kết luận an toàn.",
                string.Join(" | ", snapshot.ReadErrors.Take(3)));

        return new(SoftwareDetectionState.NotInstalled, "Không tìm thấy bằng chứng ứng dụng đã cài.");
    }

    internal static bool InstalledNameMatches(AppDefinition app, string displayName)
    {
        var name = Regex.Replace(displayName ?? "", @"\s+", " ").Trim();
        if (name.Length == 0 || app.Name.Trim().Length == 0) return false;
        var id = app.Id.Trim().ToLowerInvariant();
        return id switch
        {
            "evkey" => Match(name, @"^EVKey(?:\s+(?:v?\d[\w.\-]*|\([^)]*\)))?$"),
            "chrome" => Match(name, @"^Google Chrome(?:\s+\d[\d.]*)?$"),
            "klite" => Match(name, @"^K-Lite Codec Pack(?:\s+\d[\d.]*)?(?:\s+(?:Basic|Standard|Full|Mega))?$"),
            "telegram" => Match(name, @"^Telegram Desktop(?:\s+\d[\d.]*)?$"),
            "ultraview" => Match(name, @"^UltraViewer(?:\s+(?:version\s+)?\d[\d.]*)?$"),
            "winrar" => Match(name, @"^WinRAR(?:\s+\d[\d.]*)?(?:\s+\((?:32|64)-bit\))?$"),
            "zalo" => Match(name, @"^Zalo(?:\s+\d[\d.]*)?$"),
            "zoom" => Match(name, @"^(?:Zoom|Zoom Workplace)(?:\s+\d[\d.]*)?(?:\s+\((?:32|64)-bit\))?$"),
            "office" => IsOfficeSuiteDisplayName(name),
            "wps" => Match(name, @"^WPS Office(?:\s+\d[\d.]*)?(?:\s+\([^)]*\))?$"),
            "vc64" => IsVisualCRedistributable(name, "x64"),
            "vc86" => IsVisualCRedistributable(name, "x86"),
            _ => Match(name, "^" + Regex.Escape(app.Name.Trim()) + @"(?:$|\s+(?:v(?:ersion)?\s*)?\d[\w.\-]*|\s*\([^)]*\)$)")
        };
    }

    private static bool IsOfficeSuiteDisplayName(string name)
    {
        if (!Match(name, @"^Microsoft (?:Office|365 Apps)\b")) return false;
        if (Match(name, @"\b(?:Visio|Project|Language Pack|Proof|Update|Click-to-Run|Extensibility|Licensing|Component)\b")) return false;
        return Match(name, @"\b(?:Home|Student|Business|Professional|ProPlus|Standard|Enterprise|Apps|Personal|Family|Mondo)\b");
    }

    private static bool IsOfficeSuiteProductId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || Match(id, @"(?:Project|Visio|LanguagePack|Proofing|Access)")) return false;
        return Match(id, @"(?:O365|M365|HomeStudent|HomeBusiness|Professional|ProPlus|Standard|Business|Personal|Family|Mondo)");
    }

    private static bool IsVisualCRedistributable(string name, string architecture) =>
        Match(name, @"^Microsoft Visual C\+\+ (?:2015-2022|v14) Redistributable \(" + architecture + @"\)(?:\s+-\s+\d[\d.]*)?$");

    private static bool IsV14(string version)
    {
        var normalized = (version ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var parsed) && parsed.Major == 14;
    }

    private static bool Match(string value, string pattern) =>
        Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static void AddKnownExecutable(InstalledSoftwareSnapshot snapshot, string id, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            // Reading version metadata rejects directory placeholders while retaining support
            // for portable installers that do not populate ProductName.
            _ = FileVersionInfo.GetVersionInfo(path);
            if (!snapshot.Executables.ContainsKey(id)) snapshot.Executables[id] = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            snapshot.ReadErrors.Add($"File {path}: {ex.Message}");
        }
    }

    private static void ReadUninstall(InstalledSoftwareSnapshot snapshot, RegistryHive hive, RegistryView view)
    {
        var source = $"{hive}/{view}/{UninstallPath}";
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallPath);
            if (uninstall == null) return;
            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var entry = uninstall.OpenSubKey(subKeyName);
                    var displayName = entry?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    snapshot.Entries.Add(new(displayName!.Trim(),
                        entry?.GetValue("DisplayVersion") as string ?? "",
                        entry?.GetValue("Publisher") as string ?? "",
                        entry?.GetValue("InstallLocation") as string ?? "",
                        source + "/" + subKeyName));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    snapshot.ReadErrors.Add($"{source}/{subKeyName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            snapshot.ReadErrors.Add($"{source}: {ex.Message}");
        }
    }

    private static void ReadOfficeClickToRun(InstalledSoftwareSnapshot snapshot, RegistryView view)
    {
        var source = $"LocalMachine/{view}/{ClickToRunPath}";
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(ClickToRunPath);
            if (key?.GetValue("ProductReleaseIds") is not string ids) return;
            foreach (var id in ids.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                snapshot.OfficeProductReleaseIds.Add(id.Trim() + $"; {source}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            snapshot.ReadErrors.Add($"{source}: {ex.Message}");
        }
    }

    private static void ReadVisualCRuntime(InstalledSoftwareSnapshot snapshot, RegistryView view, string architecture)
    {
        var keyPath = VcRuntimePath + "\\" + architecture;
        var source = $"LocalMachine/{view}/{keyPath}";
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(keyPath);
            if (key == null) return;
            var installed = Convert.ToInt32(key.GetValue("Installed", 0)) == 1;
            var version = key.GetValue("Version") as string ?? "";
            snapshot.VisualCRuntimes.Add(new(architecture, installed, version, source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or FormatException)
        {
            snapshot.ReadErrors.Add($"{source}: {ex.Message}");
        }
    }
}
#endif
