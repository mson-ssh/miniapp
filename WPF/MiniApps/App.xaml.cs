using System.Windows;
using MiniApps.Models;
using MiniApps.Services;
using MiniApps.ViewModels;

namespace MiniApps;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var configRoot = Option(e.Args, "--config-root");
        var headless = e.Args.Contains("--validate-config") || e.Args.Contains("--initialize-release-config");
        try
        {
#if MINIAPPS_DEVELOPER
            if (e.Args.Contains("--initialize-release-config"))
            {
                if (string.IsNullOrWhiteSpace(configRoot)) throw new ArgumentException("Thiếu --config-root cho cấu hình phát hành.");
                var initial = new SettingsStore(configRoot);
                if (!File.Exists(Path.Combine(configRoot, "apps.json"))) initial.Save(Catalog.Defaults());
                if (!File.Exists(Path.Combine(configRoot, "windows.json"))) initial.SaveWindows(WindowsSettingsCatalog.Defaults());
                Shutdown(0);
                return;
            }
#endif
            if (e.Args.Contains("--validate-config"))
            {
                if (string.IsNullOrWhiteSpace(configRoot)) throw new ArgumentException("Thiếu --config-root để kiểm tra cấu hình.");
                var validation = new SettingsStore(configRoot);
                validation.Load(required: true);
                validation.LoadWindows(required: true);
                Shutdown(0);
                return;
            }

#if MINIAPPS_DEVELOPER
            var developerPreview = e.Args.Contains("--developer-preview");
            var preview = developerPreview || e.Args.Contains("--preview");
            var model = new MainViewModel(preview, developerEdition: true,
                settingsDirectory: configRoot, settingsWritable: developerPreview);
#else
            var preview = e.Args.Contains("--preview");
            configRoot = Path.Combine(AppContext.BaseDirectory, "ReleaseConfig");
            var model = new MainViewModel(preview, developerEdition: false,
                settingsDirectory: configRoot, requireSettings: true);
#endif
            var window = new MainWindow(model);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
#if NET48
            var startupLog = StartupFailureLog.TryWrite(ex);
            if (!headless)
                MessageBox.Show(StartupFailureLog.BuildUserMessage(ex, startupLog), "MiniApps - lỗi khởi động", MessageBoxButton.OK, MessageBoxImage.Error);
#else
            if (!headless) MessageBox.Show(ex.Message, "MiniApps - cấu hình không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
            Shutdown(2);
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
