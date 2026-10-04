using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MiniApps.Models;
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

public sealed class AppDefinition : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string name = "";
    public string Name { get => name; set => Set(ref name, value); }
    private string url = "";
    public string Url { get => url; set => Set(ref url, value); }
    private string arguments = "";
    public string Arguments { get => arguments; set => Set(ref arguments, value); }
    private string sha256 = "";
    public string Sha256 { get => sha256; set => Set(ref sha256, value); }
    private string suite = "";
    public string Suite { get => suite; set => Set(ref suite, value); }
    // Every installer is awaited without the apps it opens. This flag also skips waiting for stages that
    // still run from the work folder after the installer exits (EVKey's self-extractor needs neither).
    private bool waitInstallerOnly;
    public bool WaitInstallerOnly { get => waitInstallerOnly; set => Set(ref waitInstallerOnly, value); }
}

public sealed class AppRow(AppDefinition definition) : Observable
{
    public AppDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public bool IsWindowsSummary => Definition.Id == "windows:summary";
    private string status = "Sẵn sàng";
    public string Status { get => status; set { if (Set(ref status, value)) { Raise(nameof(ProgressText)); Raise(nameof(IsInstalling)); } } }
    private double progress;
    public double Progress { get => progress; set { if (Set(ref progress, value)) Raise(nameof(ProgressText)); } }
    public bool IsInstalling => Status.StartsWith("Đang cài", StringComparison.Ordinal) || Status == "Đang áp dụng";
    public string ProgressText => IsInstalling ? "Đang chạy…"
        : Status.StartsWith("Thất bại", StringComparison.Ordinal) || Status == "Đã hủy" ? "—"
        : Status.StartsWith("Chờ cài", StringComparison.Ordinal) ? "Đã tải"
        : $"{Progress:0}%";
}

public sealed class WindowsOption(WindowsSettingDefinition definition) : Observable
{
    public WindowsSettingDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
}

public static class Catalog
{
    // One R2 bucket, served first through the cached custom domain. The bucket's r2.dev address and a
    // GitHub release holding the same file names are fallbacks for the same files.
    public const string Primary = "https://dl.miniaz.io.vn";
    public const string R2 = "https://pub-50d6cf4af6964541b0621bbc9bc26690.r2.dev";
    public const string GitHubMirror = "https://github.com/mson-ssh/miniapp/releases/download/installers";
    // Sources to try in order. A URL on either bucket address gets all three, so configs saved with
    // the r2.dev address use the custom domain too; any other URL is used as it is.
    public static IReadOnlyList<string> DownloadSources(string url)
    {
        foreach (var host in new[] { Primary, R2 })
        {
            if (!url.StartsWith(host + "/", StringComparison.OrdinalIgnoreCase)) continue;
            var path = url.Substring(host.Length);
            return [Primary + path, R2 + path, GitHubMirror + "/" + Path.GetFileName(new Uri(url).AbsolutePath)];
        }
        return [url];
    }
    public static List<AppDefinition> Defaults() =>
    [
        Make("evkey", "EVKey", "EVKey.exe", "-s", waitInstallerOnly: true),
        Make("chrome", "Google Chrome", "chrome.exe", "/silent /install"),
        Make("klite", "K-Lite Codec Pack", "klite.exe", "/verysilent /norestart /suppressmsgboxes"),
        Make("telegram", "Telegram", "tele.exe", "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES"),
        Make("ultraview", "UltraViewer", "ultrav.exe", "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES"),
        Make("winrar", "WinRAR", "winrar.exe", "/S"),
        Make("zalo", "Zalo", "zalo.exe", "/S"),
        Make("zoom", "Zoom", "zoom.exe", "/silent"),
        Make("office", "Office 2024", "OfficeSetup.exe", "", "Office"),
        Make("wps", "WPS Office", "wps.exe", "/S", "WPS"),
        // Both installers exceed R2's 300 MB file limit, so they come from the vendors.
        // OnlyOffice's URL always serves the latest release, so it cannot carry a hash.
        new() { Id = "onlyoffice", Name = "OnlyOffice", Suite = "OnlyOffice", Arguments = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES",
            Url = "https://download.onlyoffice.com/install/desktop/editors/windows/distrib/onlyoffice/DesktopEditors_x64.exe" },
        // TDF mirror (the archive server ran ~1 MB/s). SHA-256 matches TDF's published hash. Mirrors drop a
        // version from stable/ once it is superseded: move to the next release or to downloadarchive then.
        new() { Id = "libreoffice", Name = "LibreOffice", Suite = "LibreOffice", Arguments = "/qn /norestart",
            Url = "https://mirror.freedif.org/TDF/libreoffice/stable/26.8.0/win/x86_64/LibreOffice_26.8.0_Win_x86-64.msi",
            Sha256 = "4aa6c6e1895f4055104effcb556bd3362d20c6ad707c149543304f395ef9db95" },
        Make("vc64", "Visual C++ x64", "VC_redist.x64.exe", "/install /quiet /norestart"),
        Make("vc86", "Visual C++ x86", "VC_redist.x86.exe", "/install /quiet /norestart")
    ];
    private static AppDefinition Make(string id, string name, string file, string args, string suite = "", bool waitInstallerOnly = false) => new()
    { Id = id, Name = name, Url = $"{Primary}/{file}", Arguments = args, Suite = suite, WaitInstallerOnly = waitInstallerOnly };
    public static void Validate(IEnumerable<AppDefinition> apps)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            if (!Regex.IsMatch(app.Id, "^[a-zA-Z0-9_-]{1,64}$") || !ids.Add(app.Id)) throw new InvalidDataException("ID ứng dụng không hợp lệ hoặc bị trùng.");
            if (string.IsNullOrWhiteSpace(app.Name)) throw new InvalidDataException("Tên ứng dụng không được để trống.");
            if (!Uri.TryCreate(app.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new InvalidDataException($"{app.Name}: URL phải là HTTPS.");
            if (!new[] { ".exe", ".msi" }.Contains(Path.GetExtension(uri.AbsolutePath).ToLowerInvariant())) throw new InvalidDataException($"{app.Name}: URL phải trỏ đến file .exe hoặc .msi.");
            if (app.Sha256.Length > 0 && !Regex.IsMatch(app.Sha256, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException($"{app.Name}: SHA-256 phải có 64 ký tự hex.");
            if (app.Suite is not ("" or "Office" or "WPS" or "OnlyOffice" or "LibreOffice")) throw new InvalidDataException("Nhóm Office không hợp lệ.");
        }
    }
}
