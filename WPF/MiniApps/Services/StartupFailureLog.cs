#if NET48
using System.Text;

namespace MiniApps.Services;

public static class StartupFailureLog
{
    public static string? TryWrite(Exception exception, string? baseDirectory = null)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));

        try
        {
            var root = string.IsNullOrWhiteSpace(baseDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniApps", "StartupLogs")
                : Path.GetFullPath(baseDirectory);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".log");
            var text = new StringBuilder()
                .AppendLine("MiniApps startup failure")
                .AppendLine("Timestamp: " + DateTimeOffset.Now.ToString("O"))
                .AppendLine("Version: " + typeof(App).Assembly.GetName().Version)
                .AppendLine("Runtime: " + Environment.Version)
                .AppendLine("OS: " + Environment.OSVersion)
                .AppendLine("Base directory: " + AppContext.BaseDirectory)
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static string BuildUserMessage(Exception exception, string? logPath)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        var message = "MiniApps không thể khởi động.\n\n" + exception.Message;
        return string.IsNullOrWhiteSpace(logPath)
            ? message + "\n\nKhông thể ghi nhật ký lỗi khởi động."
            : message + "\n\nNhật ký lỗi:\n" + logPath;
    }
}
#endif
