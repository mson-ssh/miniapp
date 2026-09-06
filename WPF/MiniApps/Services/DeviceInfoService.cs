using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace MiniApps.Services;

public sealed record DeviceInfo(string Host, string Brand, string Model, string Serial, string DriverUrl);

public static class DeviceInfoService
{
    public static async Task<DeviceInfo> ReadAsync()
    {
        var host = Environment.MachineName;
        var brand = ReadBiosValue("SystemManufacturer");
        var model = ReadBiosValue("SystemProductName");
        var serial = "";
        try
        {
            const string script = "$cs=Get-CimInstance Win32_ComputerSystem -ErrorAction Stop;$bios=Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue;[pscustomobject]@{Host=$env:COMPUTERNAME;Brand=[string]$cs.Manufacturer;Model=[string]$cs.Model;Serial=[string]$bios.SerialNumber}|ConvertTo-Json -Compress";
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Arguments = ProcessCompatibility.JoinArguments(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) });
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Windows PowerShell.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await ProcessCompatibility.WaitForExitAsync(process, timeout.Token); }
            catch (OperationCanceledException)
            {
                // This is the diagnostic PowerShell process owned by this call. Never terminate installers.
                try { if (!process.HasExited) process.Kill(); } catch { }
                try
                {
                    var exit = ProcessCompatibility.WaitForExitAsync(process);
                    await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(2)));
                }
                catch { }
                throw;
            }
            var output = await outputTask;
            _ = await errorTask;
            if (process.ExitCode == 0)
            {
                var data = JsonSerializer.Deserialize<CimDeviceInfo>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                host = Clean(data?.Host, host);
                brand = Clean(data?.Brand, brand);
                model = Clean(data?.Model, model);
                serial = Clean(data?.Serial, "");
            }
        }
        catch { /* Registry and environment values remain available as a safe fallback. */ }
        return Resolve(host, brand, model, serial);
    }

    public static DeviceInfo Resolve(string host, string brand, string model, string serial)
    {
        host = Clean(host, Environment.MachineName);
        brand = Clean(brand, "Không xác định");
        model = Clean(model, "Không xác định");
        serial = Clean(serial, "Không xác định");
        var key = brand.ToLowerInvariant();
        var (canonical, url) = key switch
        {
            var x when x.Contains("dell") => ("Dell", "https://www.dell.com/support/home/en-us?app=drivers"),
            var x when x.Contains("hewlett") || x == "hp" || x.StartsWith("hp ") => ("HP", "https://support.hp.com/us-en/drivers"),
            var x when x.Contains("lenovo") => ("Lenovo", "https://pcsupport.lenovo.com/us/en/"),
            var x when x.Contains("asus") || x.Contains("asustek") => ("ASUS", "https://www.asus.com/us/support/download-center/"),
            var x when x.Contains("acer") || x.Contains("gateway") => ("Acer", "https://www.acer.com/us-en/support/drivers-and-manuals"),
            var x when x.Contains("micro-star") || x == "msi" || x.StartsWith("msi ") => ("MSI", "https://www.msi.com/support/download"),
            var x when x.Contains("microsoft") => ("Microsoft Surface", "https://support.microsoft.com/en-us/surface/drivers-firmware/download-drivers-and-firmware-for-surface"),
            var x when x.Contains("samsung") => ("Samsung", "https://www.samsung.com/us/support/downloads/"),
            _ => (brand, "")
        };
        return new(host, canonical, model, serial, url);
    }

    private static string ReadBiosValue(string name)
    {
        try { return Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS")?.GetValue(name)?.ToString() ?? ""; }
        catch { return ""; }
    }

    private static string Clean(string? value, string fallback)
    {
        var result = value?.Trim() ?? "";
        var placeholders = new[] { "To Be Filled By O.E.M.", "Default string", "System Serial Number", "System Product Name", "System Manufacturer", "Unknown", "None", "N/A" };
        return result.Length == 0 || placeholders.Contains(result, StringComparer.OrdinalIgnoreCase) ? fallback : result;
    }

    private sealed class CimDeviceInfo
    {
        public string Host { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string Serial { get; set; } = "";
    }
}
