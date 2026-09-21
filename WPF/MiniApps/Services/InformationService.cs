using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MiniApps.Services;

internal sealed record InformationRow(string Name, string Value)
{
    public string Summary => Value.Split('\n')[0].TrimEnd('\r');
    public string Details => Value.IndexOf('\n') is var index && index >= 0 ? Value.Substring(index + 1).TrimEnd() : "";
    public string Total { get; init; } = "—";
    public string DriverUrl { get; init; } = "";
    public IReadOnlyList<RamModule> Ram { get; init; } = Array.Empty<RamModule>();
    public IReadOnlyList<GraphicsAdapter> Gpu { get; init; } = Array.Empty<GraphicsAdapter>();
    public IReadOnlyList<StorageDevice> Disks { get; init; } = Array.Empty<StorageDevice>();
    private string RamValues(Func<RamModule, string> selector) => string.Join(" / ", Ram.Select(selector).Distinct());
    public string RamSummary => Ram.Count == 0 ? Total.Replace(" ", "") :
        string.Join("  ", RamValues(module => module.FormFactor), RamValues(module => module.Type), Total.Replace(" ", ""),
            RamValues(module => module.Manufacturer), RamValues(module => module.DisplaySpeed));
    public IEnumerable<RamDisplayRow> RamDisplayRows => Ram.Select((module, index) => new RamDisplayRow($"SLOT {index + 1}:",
        module.Type, module.CompactCapacity, module.Manufacturer, module.DisplaySpeed));
}
internal sealed record RamDisplayRow(string Slot, string Type, string Capacity, string Manufacturer, string Speed);

internal static class InformationService
{
    internal static async Task<IReadOnlyList<InformationRow>> ReadAsync(CancellationToken token)
    {
        using var resource = typeof(InformationService).Assembly.GetManifestResourceStream("MiniApps.Information.ps1")
            ?? throw new InvalidOperationException("Thiếu script Information.");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        var script = await reader.ReadToEndAsync();
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Không mở được PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        // Base64 preserves Vietnamese text through Windows PowerShell's stdin encoding.
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        await process.StandardInput.WriteLineAsync("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding; & ([scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')))) -AsJson");
        process.StandardInput.Close();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try { await ProcessCompatibility.WaitForExitAsync(process, deadline.Token); }
        catch (OperationCanceledException)
        {
            // Only terminate the read-only diagnostic process owned by this request.
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("Đọc thông tin quá 90 giây. Hãy thử đọc lại.");
        }
        var json = await output;
        var errors = await error;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Không đọc được thông tin hệ thống. " + errors.Trim());
        return Parse(json);
    }

    internal static IReadOnlyList<InformationRow> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement;
        var fields = new[] { ("OS", "Windows"), ("Hostname", "Tên máy"), ("Model", "Model"),
            ("Serial", "Serial"), ("CPU", "CPU"), ("RAM", "RAM"), ("GraphicsCard", "Graphics Card"),
            ("Storage", "Storage"), ("Resolution", "Độ phân giải"), ("RefreshRate", "Tần số quét tối đa"), ("DateTime", "Thời điểm đọc") };
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("OS", out _))
            throw new InvalidDataException("Dữ liệu Information không hợp lệ.");
        var rows = new List<InformationRow>();
        foreach (var (key, name) in fields)
        {
            var value = data.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            rows.Add(new(name, string.IsNullOrWhiteSpace(value) ? "Không xác định" : value!)
            {
                Total = key == "RAM" ? InformationHardware.Text(data, "RamTotal") : "—",
                DriverUrl = key == "Serial" ? DeviceInfoService.Resolve("", InformationHardware.Text(data, "Manufacturer"), "", value ?? "").DriverUrl : "",
                Ram = key == "RAM" ? InformationHardware.Ram(data) : Array.Empty<RamModule>(),
                Gpu = key == "GraphicsCard" ? InformationHardware.Gpu(data) : Array.Empty<GraphicsAdapter>(),
                Disks = key == "Storage" ? InformationHardware.Disks(data) : Array.Empty<StorageDevice>()
            });
            if (key == "OS") rows.Add(new("Bản quyền Windows", data.TryGetProperty("IsActivated", out var active) && active.ValueKind == JsonValueKind.True
                ? "Đã kích hoạt" : "Chưa xác nhận kích hoạt"));
        }
        return rows;
    }
}
