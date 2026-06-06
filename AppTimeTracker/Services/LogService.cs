using System.IO;
using System.Text;

namespace AppTimeTracker.Services;

public static class LogService
{
    private const long MaxLogFileBytes = 5 * 1024 * 1024;
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static Task InfoAsync(string message) => WriteAsync("INFO", message);
    public static Task WarningAsync(string message) => WriteAsync("WARN", message);
    public static Task ErrorAsync(string message) => WriteAsync("ERROR", message);

    public static Task ExceptionAsync(string message, Exception exception)
    {
        return WriteAsync("ERROR", $"{message}{Environment.NewLine}Exception: {exception}");
    }

    public static void Info(string message) => _ = InfoAsync(message);
    public static void Warning(string message) => _ = WarningAsync(message);
    public static void Error(string message) => _ = ErrorAsync(message);
    public static void Exception(string message, Exception exception) => _ = ExceptionAsync(message, exception);

    private static async Task WriteAsync(string level, string message)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var path = GetLogPath();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

            await WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(path, line, Encoding.UTF8).ConfigureAwait(false);
            }
            finally
            {
                WriteLock.Release();
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private static string GetLogPath()
    {
        var basePath = Path.Combine(AppPaths.LogsDirectory, $"app_{DateTime.Now:yyyy-MM-dd}.log");
        if (!File.Exists(basePath) || new FileInfo(basePath).Length < MaxLogFileBytes)
        {
            return basePath;
        }

        return Path.Combine(AppPaths.LogsDirectory, $"app_{DateTime.Now:yyyy-MM-dd_HHmmss}.log");
    }
}
