using System.Text.Json;
using MiniApps.Models;

namespace MiniApps.Services;
public sealed class SettingsStore(string? directory = null)
{
    private const int AppsSchemaVersion = 1;
    private const int WindowsSchemaVersion = 2;
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
    public void Save(IEnumerable<AppDefinition> apps)
    {
        var items = apps.ToList();
        Catalog.Validate(items);
        var present = items.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = Catalog.Defaults().Where(x => !present.Contains(x.Id)).Select(x => x.Id).ToList();
        Write("apps.json", new SettingsEnvelope<AppDefinition>(AppsSchemaVersion, items, removed));
    }
    public List<WindowsSettingDefinition> LoadWindows(bool required = false)
    {
        var path = Path.Combine(DirectoryPath, "windows.json");
        if (required && !File.Exists(path)) throw new FileNotFoundException("Thiếu cấu hình Windows đã đóng gói.", path);
        if (!File.Exists(path)) return WindowsSettingsCatalog.Defaults();
        var saved = Read<WindowsSettingDefinition>(path, WindowsSchemaVersion, legacyVersion: 1);
        var settings = saved.Items;
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
    public void SaveWindows(IEnumerable<WindowsSettingDefinition> settings)
    {
        var items = settings.ToList();
        WindowsSettingsCatalog.Validate(items);
        var present = items.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = WindowsSettingsCatalog.Defaults().Where(x => !present.Contains(x.Id)).Select(x => x.Id).ToList();
        Write("windows.json", new SettingsEnvelope<WindowsSettingDefinition>(WindowsSchemaVersion, items, removed));
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
    private void Write<T>(string filename, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, filename);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path))
            {
                // Same-volume replacement is atomic; on failure the existing target remains intact.
                File.Replace(temp, path, null, true);
            }
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}

public sealed record SettingsEnvelope<T>(int SchemaVersion, List<T> Items, List<string> RemovedDefaultIds);
