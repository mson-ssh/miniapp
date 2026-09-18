using System.Text.Json;
using MiniApps.Models;

namespace MiniApps.Services;
public sealed class SettingsStore(string? directory = null)
{
    private const int AppsSchemaVersion = 1;
    private const int WindowsSchemaVersion = 3;
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps");
    public List<AppDefinition> Load(bool required = false)
    {
        var path = Path.Combine(DirectoryPath, "apps.json");
        if (required && !File.Exists(path)) throw new FileNotFoundException("Thiếu cấu hình ứng dụng đã đóng gói.", path);
        if (!File.Exists(path)) return Catalog.Defaults();
        var apps = Read<AppDefinition>(path, AppsSchemaVersion).Items;
        Catalog.Validate(apps);
        return apps;
    }
    public List<WindowsSettingDefinition> LoadWindows(bool required = false)
    {
        var path = Path.Combine(DirectoryPath, "windows.json");
        if (required && !File.Exists(path)) throw new FileNotFoundException("Thiếu cấu hình Windows đã đóng gói.", path);
        if (!File.Exists(path)) return WindowsSettingsCatalog.Defaults();
        var saved = Read<WindowsSettingDefinition>(path, WindowsSchemaVersion, legacyVersion: 1);
        var settings = saved.Items;
        if (saved.SchemaVersion < 3)
            settings.RemoveAll(setting => setting != null &&
                (setting.Id.Equals("Debloat", StringComparison.OrdinalIgnoreCase) ||
                 setting.Action.Equals("Debloat", StringComparison.OrdinalIgnoreCase)));
        // Old configs stored only an Action for built-ins. Materialize their commands once on load.
        foreach (var setting in settings.Where(s => s != null && s.Action != "Custom" && string.IsNullOrEmpty(s.Script)))
            setting.Script = WindowsSettingsCatalog.DefaultScript(setting.Action);
        var present = settings.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = saved.RemovedDefaultIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in WindowsSettingsCatalog.Defaults())
        {
            var introducedIn = item.Id == "InfoExe" ? 2 : 1;
            if (introducedIn > saved.SchemaVersion && !present.Contains(item.Id) && !removed.Contains(item.Id)) settings.Add(item);
        }
        WindowsSettingsCatalog.Validate(settings);
        return settings;
    }
    private static SettingsEnvelope<T> Read<T>(string path, int currentVersion, int legacyVersion = 1)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        SettingsEnvelope<T> result;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var items = JsonSerializer.Deserialize<List<T>>(json) ?? throw new InvalidDataException("Cấu hình rỗng.");
            result = new SettingsEnvelope<T>(legacyVersion, items, []);
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            result = JsonSerializer.Deserialize<SettingsEnvelope<T>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Cấu hình rỗng.");
        }
        else throw new InvalidDataException("Định dạng cấu hình không hợp lệ.");
        if (result.SchemaVersion < 1 || result.SchemaVersion > currentVersion)
            throw new InvalidDataException($"Phiên bản cấu hình {result.SchemaVersion} chưa được hỗ trợ.");
        if (result.Items == null || result.RemovedDefaultIds == null) throw new InvalidDataException("Cấu hình thiếu dữ liệu.");
        return result;
    }
}

public sealed record SettingsEnvelope<T>(int SchemaVersion, List<T> Items, List<string> RemovedDefaultIds);
