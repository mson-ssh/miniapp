using Microsoft.Win32;
using MiniApps.Models;

namespace MiniApps.Services;

public static class WindowsCompatibility
{
    public const int MinimumWindowsBuild = 17763;

    public static int CurrentBuild => ReadInstalledBuild();

    public static int MinimumBuildFor(WindowsSettingDefinition setting) => setting.Action switch
    {
        // WinGet/App Installer officially supports Windows 10 version 1809 and later.
        "Winget" => 17763,
        // The remaining built-ins use Windows PowerShell, registry, powercfg, networking
        // and download APIs already available at the MiniApps Windows baseline.
        "Desktop" or "Timezone" or "Dns" or "FastStartup" or "Power" or
        "PasswordExpiry" or "InfoExe" => 17763,
        // User scripts have no machine-readable compatibility metadata yet.
        _ => MinimumWindowsBuild
    };

    public static bool Supports(WindowsSettingDefinition setting, int windowsBuild) =>
        windowsBuild >= MinimumBuildFor(setting);

    internal static int ReadInstalledBuild()
    {
        var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?? throw new InvalidOperationException("Không thể đọc phiên bản Windows đã cài đặt.");
        var text = Convert.ToString(key.GetValue("CurrentBuildNumber", key.GetValue("CurrentBuild", "")));
        if (!int.TryParse(text, out var build))
            throw new InvalidOperationException($"Không thể xác định Windows build từ '{text}'.");
        return build;
    }
}
