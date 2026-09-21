using System.Globalization;
using System.Text.Json;

namespace MiniApps.Services;

internal sealed record RamModule(string Slot, string Capacity, string Specification, string Manufacturer)
{
    public string Type { get; init; } = "—";
    public string Speed { get; init; } = "—";
    public string FormFactor { get; init; } = "—";
    public string CompactCapacity => Capacity.Replace(" ", "");
    public string DisplaySpeed => Speed.Replace(" MT/s", "Mhz").Replace(" MHz", "Mhz");
}
internal sealed record GraphicsAdapter(string Name, string Kind, string Memory, string Power);
internal sealed record StorageDevice(string Model, string Capacity, string Connection, IReadOnlyList<StorageVolume> Partitions);
internal sealed record StorageVolume(string Letter, double? FreeBytes, double? TotalBytes)
{
    public bool HasCapacity => TotalBytes > 0 && FreeBytes >= 0 && FreeBytes <= TotalBytes;
    public double UsedPercent => HasCapacity ? (1 - FreeBytes!.Value / TotalBytes!.Value) * 100 : 0;
    public string CapacityText => HasCapacity
        ? $"{Format(FreeBytes!.Value)} GB trống / {Format(TotalBytes!.Value)} GB"
        : "—";
    private static string Format(double bytes) => (bytes / 1073741824d).ToString("0.#", CultureInfo.InvariantCulture);
}

internal static class InformationHardware
{
    internal static string Text(JsonElement data, string name) => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : "—";
    private static double? Number(JsonElement data, string name) => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && !double.IsNaN(number) && !double.IsInfinity(number) ? number : null;
    private static IEnumerable<JsonElement> Items(JsonElement data, string name) =>
        data.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray() : Array.Empty<JsonElement>();
    internal static IReadOnlyList<RamModule> Ram(JsonElement data) => Items(data, "RamItems").Select(item =>
        new RamModule(Text(item, "Slot"), Text(item, "Capacity"),
            string.Join(" · ", new[] { Text(item, "Type"), Text(item, "Speed"), Text(item, "FormFactor") }.Where(value => value != "—")) is var spec && spec.Length > 0 ? spec : "—",
            Text(item, "Manufacturer"))
        { Type = Text(item, "Type"), Speed = Text(item, "Speed"), FormFactor = Text(item, "FormFactor") }).ToArray();
    internal static IReadOnlyList<GraphicsAdapter> Gpu(JsonElement data) => Items(data, "GpuItems").Select(item =>
        new GraphicsAdapter(Text(item, "Name"), Text(item, "Kind"), Text(item, "Memory"), Text(item, "Power"))).ToArray();
    internal static IReadOnlyList<StorageDevice> Disks(JsonElement data) => Items(data, "StorageItems").Select(item =>
        new StorageDevice(Text(item, "Model"), Text(item, "Capacity"), Text(item, "Connection"),
            Items(item, "Partitions").Select(part => new StorageVolume(Text(part, "Letter"), Number(part, "FreeBytes"), Number(part, "TotalBytes"))).ToArray())).ToArray();
}
