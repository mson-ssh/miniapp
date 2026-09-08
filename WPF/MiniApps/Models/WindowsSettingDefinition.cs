using System.Text.RegularExpressions;

namespace MiniApps.Models;

public sealed class WindowsSettingDefinition : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string name = "";
    public string Name { get => name; set => Set(ref name, value); }
    private string description = "";
    public string Description { get => description; set => Set(ref description, value); }
    private string action = "Custom";
    public string Action { get => action; set { if (Set(ref action, value)) Raise(nameof(IsCustom)); } }
    private string script = "";
    public string Script { get => script; set => Set(ref script, value); }
    public bool IsCustom => Action == "Custom";
    // Separate event namespace: a setting and an app may have the same user-defined ID.
    public string TaskId => "windows:" + Id;
}

public sealed record WindowsAction(string Id, string Label);

public static class WindowsSettingsCatalog
{
    public static IReadOnlyList<WindowsAction> Actions { get; } =
    [
        new("Desktop", "Hiện biểu tượng Desktop"), new("Timezone", "Múi giờ Việt Nam"),
        new("Dns", "DNS Cloudflare / Google"), new("FastStartup", "Tắt Fast Startup"),
        new("Power", "Không tắt màn hình / sleep"), new("PasswordExpiry", "Mật khẩu không hết hạn"),
        new("Winget", "Winget (update)"), new("InfoExe", "Info.exe"),
        new("Custom", "PowerShell tùy chỉnh")
    ];
    public static List<WindowsSettingDefinition> Defaults() =>
    [
        Make("Desktop", "Hiện biểu tượng Desktop", "This PC, Control Panel và thư mục người dùng."),
        Make("Timezone", "Múi giờ Việt Nam", "Đặt UTC+07:00 (Bangkok, Hà Nội, Jakarta)."),
        Make("Dns", "DNS Cloudflare / Google", "Đổi DNS trên các card mạng vật lý đang kết nối."),
        Make("FastStartup", "Tắt Fast Startup", "Tắt cơ chế khởi động nhanh của Windows."),
        Make("Power", "Không tự tắt màn hình / sleep", "Áp dụng khi dùng nguồn điện và pin; tăng tiêu thụ pin."),
        Make("PasswordExpiry", "Mật khẩu không hết hạn", "Thay đổi chính sách hết hạn mật khẩu cục bộ."),
        Make("Winget", "Winget (update)", "Cập nhật WinGet / App Installer và dependency Microsoft; không upgrade toàn bộ ứng dụng."),
        Make("InfoExe", "Info.exe", "Tải info.exe về Desktop; không tự động mở ứng dụng.")
    ];
    private static WindowsSettingDefinition Make(string action, string name, string description) => new()
    { Id = action, Action = action, Name = name, Description = description, Script = DefaultScript(action) };
    public static string DefaultScript(string action) => action switch
    {
        "Desktop" => """
            $ErrorActionPreference = 'Stop'
            $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
            New-Item -Path $key -Force | Out-Null
            foreach ($id in @('{20D04FE0-3AEA-1069-A2D8-08002B30309D}', '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}', '{59031a47-3f72-44a7-89c5-5595fe6b30ee}')) {
                New-ItemProperty -Path $key -Name $id -PropertyType DWord -Value 0 -Force | Out-Null
            }
            """,
        "Timezone" => "Set-TimeZone -Id 'SE Asia Standard Time' -ErrorAction Stop",
        "Dns" => """
            $ErrorActionPreference = 'Stop'
            $adapters = @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up')
            if ($adapters.Count -eq 0) { throw 'No active physical network adapter.' }
            foreach ($adapter in $adapters) {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses @('1.1.1.1','8.8.8.8')
            }
            """,
        "FastStartup" => "New-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name HiberbootEnabled -Value 0 -PropertyType DWord -Force -ErrorAction Stop | Out-Null",
        "Power" => """
            foreach ($setting in @('monitor-timeout-ac','monitor-timeout-dc','standby-timeout-ac','standby-timeout-dc')) {
                powercfg /change $setting 0
                if ($LASTEXITCODE -ne 0) { throw "powercfg failed: $setting ($LASTEXITCODE)" }
            }
            """,
        "PasswordExpiry" => "net accounts /maxpwage:unlimited; if ($LASTEXITCODE -ne 0) { throw \"net accounts failed ($LASTEXITCODE)\" }",
        "Winget" => ReadScriptResource("Update-Winget.ps1"),
        "InfoExe" => """
            $ErrorActionPreference = 'Stop'
            $url = 'https://pub-50d6cf4af6964541b0621bbc9bc26690.r2.dev/info.exe'
            $desktop = [Environment]::GetFolderPath('Desktop')
            if ([string]::IsNullOrWhiteSpace($desktop)) { throw 'Cannot resolve the Desktop folder.' }
            $destination = Join-Path $desktop 'info.exe'
            $staged = Join-Path ([IO.Path]::GetTempPath()) ('MiniApps-info-' + [guid]::NewGuid().ToString('N') + '.exe')
            try {
                Invoke-WebRequest -Uri $url -OutFile $staged -UseBasicParsing -ErrorAction Stop
                $stream = [IO.File]::OpenRead($staged)
                try {
                    if ($stream.Length -lt 2 -or $stream.ReadByte() -ne 0x4D -or $stream.ReadByte() -ne 0x5A) {
                        throw 'Downloaded file is not a Windows executable.'
                    }
                }
                finally { $stream.Dispose() }
                Copy-Item -LiteralPath $staged -Destination $destination -Force -ErrorAction Stop
            }
            finally { Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue }
            """,
        _ => ""
    };
    private static string ReadScriptResource(string filename)
    {
        using var stream = typeof(WindowsSettingsCatalog).Assembly.GetManifestResourceStream("MiniApps.Scripts." + filename)
            ?? throw new InvalidDataException($"Missing default script: {filename}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static void Validate(IEnumerable<WindowsSettingDefinition> settings)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings)
        {
            if (setting == null || string.IsNullOrWhiteSpace(setting.Id) || !Regex.IsMatch(setting.Id, "^[a-zA-Z0-9_-]{1,64}$") || !ids.Add(setting.Id))
                throw new InvalidDataException("ID thiết lập không hợp lệ hoặc bị trùng.");
            if (string.IsNullOrWhiteSpace(setting.Name)) throw new InvalidDataException("Tên thiết lập không được để trống.");
            if (!Actions.Any(a => a.Id == setting.Action)) throw new InvalidDataException($"{setting.Name}: tác vụ không hợp lệ.");
            if (string.IsNullOrWhiteSpace(setting.Script)) throw new InvalidDataException($"{setting.Name}: cần nhập script PowerShell.");
        }
    }
}
