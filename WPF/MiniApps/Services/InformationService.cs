using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MiniApps.Services;

// Unknown values are "—"; an activation that cannot be confirmed is never shown as a licence failure.
internal sealed record InformationReport(string Host, string Windows, bool Activated, string Maker, string Serial, string ReadAt)
{
    public string Activation => Activated ? "Đã kích hoạt" : "Chưa xác nhận kích hoạt";
    public string DriverUrl => DeviceInfoService.Resolve("", Maker, "", Serial).DriverUrl;
}

// Reads straight from Windows (registry, SMBIOS firmware table, licensing API): no PowerShell, WMI or wmic,
// which newer Windows builds no longer ship. Each read takes a few milliseconds.
internal static class InformationService
{
    // Shown by --preview and used by the tests; no hardware is read.
    internal static readonly InformationReport Preview = new("MINI-PC", "Windows 11 Pro", true, "LENOVO", "DEMO-123456", "2026-09-21 18:00:00");

    internal static async Task<InformationReport> ReadAsync(CancellationToken token)
    {
        var read = Task.Run(Read, token);
        // The licensing call goes through the Software Protection service; never wait on it forever.
        var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(15), token));
        token.ThrowIfCancellationRequested();
        if (finished != read) throw new TimeoutException("Đọc thông tin quá 15 giây. Hãy thử đọc lại.");
        return await read;
    }

    internal static InformationReport Read()
    {
        var (maker, serial) = Try(ReadSmbiosSystem, ("", ""));
        return new(Known(Environment.MachineName), Known(Try(ReadWindowsName, "")), Try(IsWindowsActivated, false),
            Known(maker), Known(serial), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch (Exception) { return fallback; }
    }
    private static string Known(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string ReadWindowsName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int.TryParse(key?.GetValue("CurrentBuild") as string, out var build);
        return WindowsName(key?.GetValue("ProductName") as string ?? "", build);
    }
    // Windows 11 still writes "Windows 10 …" as ProductName; build 22000 is the first Windows 11 build.
    internal static string WindowsName(string productName, int build) =>
        build >= 22000 ? productName.Replace("Windows 10", "Windows 11") : productName;

    private static (string Maker, string Serial) ReadSmbiosSystem()
    {
        const uint Rsmb = 0x52534D42; // 'RSMB'
        var size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        if (size == 0) return ("", "");
        var table = new byte[size];
        return GetSystemFirmwareTable(Rsmb, 0, table, size) == size ? ParseSmbiosSystem(table) : ("", "");
    }
    // The SMBIOS System Information structure (type 1) is what WMI reports as the machine's Manufacturer and serial.
    internal static (string Maker, string Serial) ParseSmbiosSystem(byte[] table)
    {
        // RawSMBIOSData: an 8-byte header, then structures; each is a formatted area followed by
        // NUL-terminated strings and one more NUL (two NULs when there are no strings).
        var at = 8;
        while (at + 4 <= table.Length)
        {
            int type = table[at], length = table[at + 1];
            if (length < 4 || at + length > table.Length) break;
            var strings = new List<string>();
            var next = at + length;
            while (next < table.Length && table[next] != 0)
            {
                var end = Array.IndexOf(table, (byte)0, next);
                if (end < 0) end = table.Length;
                strings.Add(Encoding.ASCII.GetString(table, next, end - next).Trim());
                next = end + 1;
            }
            if (type == 1 && length >= 8)
            {
                string Text(int offset) => table[at + offset] is var index && index > 0 && index <= strings.Count ? strings[index - 1] : "";
                return (Text(4), Text(7));
            }
            if (type == 127) break;
            at = (strings.Count == 0 ? next + 1 : next) + 1;
        }
        return ("", "");
    }

    private static bool IsWindowsActivated()
    {
        var windows = new Guid("55c92734-d682-4d71-983e-d6ec3f16059f");
        return SLIsGenuineLocal(ref windows, out var state, IntPtr.Zero) == 0 && state == 0; // SL_GEN_STATE_IS_GENUINE
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);
    [DllImport("slwga.dll")]
    private static extern int SLIsGenuineLocal(ref Guid appId, out int state, IntPtr options);
}
