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

public enum OptimizeTaskState
{
    Ready,
    Waiting,
    Running,
    Succeeded,
    Skipped,
    Failed,
#if NET48
    Overdue,
#endif
    NotConfirmed
}

public sealed class OptimizePreviewRow(string id, string name, string description) : Observable
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string Category => Id.Equals("RemoveApps", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("remove-apps", StringComparison.OrdinalIgnoreCase) ? "Debloatware" : "Optimize Windows";
    private string status = "Sẵn sàng";
    public string Status { get => status; set => Set(ref status, value); }
    private OptimizeTaskState state = OptimizeTaskState.Ready;
    public OptimizeTaskState State
    {
        get => state;
        set
        {
            if (!Set(ref state, value)) return;
            Raise(nameof(IsRunning)); Raise(nameof(IsSucceeded)); Raise(nameof(IsSkipped)); Raise(nameof(IsFailed)); Raise(nameof(IsOverdue)); Raise(nameof(IsNotConfirmed)); Raise(nameof(IsWaiting));
        }
    }
    public bool IsRunning => State == OptimizeTaskState.Running;
    public bool IsSucceeded => State == OptimizeTaskState.Succeeded;
    public bool IsSkipped => State == OptimizeTaskState.Skipped;
    public bool IsFailed => State == OptimizeTaskState.Failed;
    public bool IsOverdue =>
#if NET48
        State == OptimizeTaskState.Overdue;
#else
        false;
#endif
    public bool IsNotConfirmed => State == OptimizeTaskState.NotConfirmed;
    public bool IsWaiting => State is OptimizeTaskState.Ready or OptimizeTaskState.Waiting;
    private double progress;
    public double Progress { get => progress; set => Set(ref progress, value); }
}

public static class OptimizeTaskCatalog
{
    public static List<OptimizePreviewRow> Defaults() =>
    [
        new("RemoveApps", "Gỡ ứng dụng mặc định", "Gỡ danh sách ứng dụng mặc định do Win11Debloat xác định."),
        new("DisableTelemetry", "Giảm dữ liệu chẩn đoán", "Tắt các thành phần telemetry trong profile Default."),
        new("DisableSuggestions", "Tắt nội dung đề xuất", "Ẩn gợi ý và nội dung quảng bá trong Windows."),
        new("DisableEdgeAds", "Tắt quảng cáo Edge", "Giảm lời mời và nội dung quảng bá của Microsoft Edge."),
        new("DisableLockscreenTips", "Tắt mẹo màn hình khóa", "Ẩn mẹo và nội dung đề xuất trên màn hình khóa."),
        new("DisableBing", "Tắt Bing trong tìm kiếm", "Giữ tìm kiếm Start tập trung vào nội dung trên máy."),
        new("DisableStoreSearchSuggestions", "Tắt gợi ý Store", "Ẩn gợi ý Microsoft Store trong kết quả tìm kiếm."),
        new("DisableCopilot", "Tắt Copilot", "Tắt tích hợp Windows Copilot theo profile Default."),
        new("DisableRecall", "Tắt Recall", "Tắt Windows Recall trên thiết bị có hỗ trợ."),
        new("DisableClickToDo", "Tắt Click to Do", "Tắt Click to Do trên thiết bị có hỗ trợ."),
        new("DisableAISvcAutoStart", "Tắt tự chạy dịch vụ AI", "Ngăn dịch vụ AI liên quan tự khởi động."),
        new("DisableWidgets", "Tắt Widgets", "Ẩn và tắt bảng Widgets trên taskbar."),
        new("HideChat", "Ẩn Chat", "Ẩn nút Chat khỏi taskbar."),
        new("ShowKnownFileExt", "Hiện đuôi tệp", "Hiện phần mở rộng của các loại tệp đã biết."),
        new("DisableDragTray", "Tắt Drag Tray", "Tắt khay chia sẻ xuất hiện khi kéo tệp."),
        new("Hide3dObjects", "Ẩn 3D Objects", "Ẩn thư mục 3D Objects khỏi Explorer."),
        new("DisableModernStandbyNetworking", "Tắt mạng khi Modern Standby", "Hạn chế kết nối mạng khi máy ở Modern Standby.")
    ];
}

public static class Catalog
{
    public const string R2 = "https://pub-50d6cf4af6964541b0621bbc9bc26690.r2.dev";
    public static List<AppDefinition> Defaults() =>
    [
        Make("evkey", "EVKey", "EVKey.exe", "-s"),
        Make("chrome", "Google Chrome", "chrome.exe", "/silent /install"),
        Make("klite", "K-Lite Codec Pack", "klite.exe", "/verysilent /norestart /suppressmsgboxes"),
        Make("telegram", "Telegram", "tele.exe", "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES"),
        Make("ultraview", "UltraViewer", "ultrav.exe", "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES"),
        Make("winrar", "WinRAR", "winrar.exe", "/S"),
        Make("zalo", "Zalo", "zalo.exe", "/S"),
        Make("zoom", "Zoom", "zoom.exe", "/silent"),
        Make("office", "Office 2024", "OfficeSetup.exe", "", "Office"),
        Make("wps", "WPS Office", "wps.exe", "/S", "WPS"),
        Make("vc64", "Visual C++ x64", "VC_redist.x64.exe", "/install /quiet /norestart"),
        Make("vc86", "Visual C++ x86", "VC_redist.x86.exe", "/install /quiet /norestart")
    ];
    private static AppDefinition Make(string id, string name, string file, string args, string suite = "") => new()
    { Id = id, Name = name, Url = $"{R2}/{file}", Arguments = args, Suite = suite };
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
            if (app.Suite is not ("" or "Office" or "WPS")) throw new InvalidDataException("Nhóm Office không hợp lệ.");
        }
    }
}
