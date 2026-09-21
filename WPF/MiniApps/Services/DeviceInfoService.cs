
namespace MiniApps.Services;

public sealed record DeviceInfo(string Host, string Brand, string Model, string Serial, string DriverUrl);

public static class DeviceInfoService
{
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

    private static string Clean(string? value, string fallback)
    {
        var result = value?.Trim() ?? "";
        var placeholders = new[] { "To Be Filled By O.E.M.", "Default string", "System Serial Number", "System Product Name", "System Manufacturer", "Unknown", "None", "N/A" };
        return result.Length == 0 || placeholders.Contains(result, StringComparer.OrdinalIgnoreCase) ? fallback : result;
    }
}
