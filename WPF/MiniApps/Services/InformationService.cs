using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MiniApps.Services;

// Tone marks a value worth noticing: "good" or "warn"; empty is neutral.
internal sealed record InformationFact(string Label, string Value, string Tone = "");
internal sealed record InformationItem(string Title, string Detail, string Tone = "")
{
    public IReadOnlyList<StorageVolume> Partitions { get; init; } = Array.Empty<StorageVolume>();
}
internal sealed record InformationSection(string Title, IReadOnlyList<InformationFact> Facts, IReadOnlyList<InformationItem> Items);

internal sealed class InformationReport
{
    public string Model { get; init; } = "—";
    public string Maker { get; init; } = "—";
    public string Serial { get; init; } = "—";
    public string DriverUrl { get; init; } = "";
    public string ReadAt { get; init; } = "—";
    public IReadOnlyList<InformationSection> Sections { get; init; } = Array.Empty<InformationSection>();
    public InformationSection? Section(string title) => Sections.FirstOrDefault(section => section.Title == title);
    public string ToText()
    {
        var text = new StringBuilder().AppendLine($"{Model} · {Maker}").AppendLine($"Serial: {Serial}");
        foreach (var section in Sections)
        {
            text.AppendLine().AppendLine(section.Title.ToUpperInvariant());
            foreach (var fact in section.Facts) text.AppendLine($"{fact.Label}: {fact.Value}");
            foreach (var item in section.Items)
            {
                text.AppendLine(item.Detail.Length == 0 ? item.Title : $"{item.Title} · {item.Detail}");
                foreach (var part in item.Partitions) text.AppendLine($"  {part.Letter}: {part.CapacityText}");
            }
        }
        return text.AppendLine().Append("Thời điểm đọc: ").Append(ReadAt).ToString();
    }
}

internal static class InformationService
{
    // Shown by --preview and used by the tests: a full machine, no hardware is read.
    internal const string PreviewJson = """
        {"OS":"Windows 11 Pro","IsActivated":true,"Hostname":"MINI-PC","Manufacturer":"LENOVO","Model":"ThinkPad T14 Gen 5","Serial":"DEMO-123456",
        "CPU":"Intel Core Ultra 7 155H","RamTotal":"32 GB",
        "RamItems":[{"Slot":"DIMM 0","Capacity":"16 GB","Type":"DDR5","Speed":"5600 MT/s","Manufacturer":"Samsung","FormFactor":"SODIMM"},{"Slot":"DIMM 1","Capacity":"16 GB","Type":"DDR5","Speed":"5600 MT/s","Manufacturer":"Samsung","FormFactor":"SODIMM"}],
        "GpuItems":[{"Name":"Intel Arc Graphics","Kind":"Tích hợp","Memory":"128 MB"},{"Name":"NVIDIA GeForce RTX 4060 Laptop GPU","Kind":"Rời","Memory":"8 GB","Power":"115 W"}],
        "StorageItems":[{"Model":"Samsung SSD 990 PRO","Capacity":"1 TB","Connection":"NVMe","Partitions":[{"Letter":"C","FreeBytes":450971566080,"TotalBytes":697932185600},{"Letter":"D","FreeBytes":193273528320,"TotalBytes":322122547200}]},
          {"Model":"Kingston SA400S37","Capacity":"480 GB","Connection":"SATA","Partitions":[{"Letter":"E","FreeBytes":225485783040,"TotalBytes":479962595328}]}],
        "Resolution":"2560 × 1600","RefreshRate":"165 Hz","DateTime":"2026-09-21 18:00:00"}
        """;

    internal static async Task<InformationReport> ReadAsync(CancellationToken token)
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

    internal static InformationReport Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("OS", out _))
            throw new InvalidDataException("Dữ liệu Information không hợp lệ.");
        string Text(string name) => InformationHardware.Text(data, name);
        var activated = data.TryGetProperty("IsActivated", out var active) && active.ValueKind == JsonValueKind.True;
        var activation = new InformationFact("Bản quyền", activated ? "Đã kích hoạt" : "Chưa xác nhận kích hoạt", activated ? "good" : "warn");
        var ram = InformationHardware.Ram(data);
        var gpus = InformationHardware.Gpu(data);
        var disks = InformationHardware.Disks(data);
        var sections = new List<InformationSection>
        {
            new("Hệ điều hành", [new("Phiên bản", Text("OS")), activation, new("Tên máy", Text("Hostname"))], []),
            new("Vi xử lý", [], [new(Text("CPU"), "")]),
            new("Bộ nhớ", Facts(
                ("Dung lượng", Text("RamTotal"), true),
                ("Loại", Join(" · ", Distinct(ram.Select(module => module.Type)), Distinct(ram.Select(module => module.Speed)), Distinct(ram.Select(module => module.FormFactor))), false)),
                ram.Select((module, index) => new InformationItem($"Khe {index + 1} · {module.Capacity}", Detail(module.Manufacturer, module.Type, module.Speed))).ToArray()),
            new("Đồ họa", [], gpus.Count > 0 ? gpus.Select(gpu => new InformationItem(gpu.Name, Detail(gpu.Kind, gpu.Memory, gpu.Power))).ToArray() : [new InformationItem("—", "")]),
            new("Lưu trữ", [], disks.Count > 0
                ? disks.Select(disk => new InformationItem(disk.Model, Detail(disk.Capacity, disk.Connection)) { Partitions = disk.Partitions }).ToArray()
                : [new InformationItem("—", "")]),
            new("Màn hình", [new("Độ phân giải tối đa", Text("Resolution")), new("Tần số quét tối đa", Text("RefreshRate"))], [])
        };
        var serial = Text("Serial");
        return new InformationReport
        {
            Model = Text("Model"), Maker = Text("Manufacturer"), Serial = serial,
            DriverUrl = DeviceInfoService.Resolve("", Text("Manufacturer"), "", serial).DriverUrl,
            ReadAt = Text("DateTime"), Sections = sections
        };
    }

    // An optional fact is left out when unknown; a required one shows "—".
    private static IReadOnlyList<InformationFact> Facts(params (string Label, string Value, bool Required)[] facts) =>
        facts.Where(fact => fact.Required || fact.Value != "—").Select(fact => new InformationFact(fact.Label, fact.Value)).ToArray();
    private static string Join(string separator, params string[] parts) =>
        string.Join(separator, parts.Where(part => part.Length > 0 && part != "—")) is { Length: > 0 } joined ? joined : "—";
    private static string Detail(params string[] parts) => Join(" · ", parts) is var detail && detail != "—" ? detail : "";
    private static string Distinct(IEnumerable<string> values) => Join(" / ", values.Distinct().ToArray());
}
