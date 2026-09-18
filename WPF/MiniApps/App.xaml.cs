using System.Windows;
using MiniApps.Services;
using MiniApps.ViewModels;

namespace MiniApps;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var configRoot = Option(e.Args, "--config-root");
        var headless = e.Args.Contains("--validate-config") || e.Args.Contains("--startup-smoke-test");
        try
        {
            if (e.Args.Contains("--validate-config"))
            {
                if (string.IsNullOrWhiteSpace(configRoot)) throw new ArgumentException("Thiếu --config-root để kiểm tra cấu hình.");
                var validation = new SettingsStore(configRoot);
                validation.Load(required: true);
                validation.LoadWindows(required: true);
                Shutdown(0);
                return;
            }

            var preview = e.Args.Contains("--preview");
            configRoot = Path.Combine(AppContext.BaseDirectory, "ReleaseConfig");
            var model = new MainViewModel(preview, settingsDirectory: configRoot, requireSettings: true);
            var window = new MainWindow(model);
            MainWindow = window;
            if (e.Args.Contains("--startup-smoke-test"))
                window.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => { window.Close(); Shutdown(0); }));
            window.Show();
        }
        catch (Exception ex)
        {
            var startupLog = StartupFailureLog.TryWrite(ex);
            if (!headless)
                MessageBox.Show(StartupFailureLog.BuildUserMessage(ex, startupLog), "MiniApps - lỗi khởi động", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
