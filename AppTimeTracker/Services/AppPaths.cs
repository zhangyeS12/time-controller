using System.IO;

namespace AppTimeTracker.Services;

public static class AppPaths
{
    public const string AppName = "time-controller";
    public const string LegacyAppName = "AppTimeTracker";
    public const string Version = "v1.11.0";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");
    public static string BackupsDirectory { get; } = Path.Combine(DataDirectory, "backups");
    public static string ExportsDirectory { get; } = Path.Combine(DataDirectory, "exports");
    public static string SettingsDirectory { get; } = Path.Combine(DataDirectory, "settings");
    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "app_usage.db");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }

    public static void MigrateLegacyDatabaseIfNeeded()
    {
        EnsureDirectories();

        if (File.Exists(DatabasePath))
        {
            return;
        }

        foreach (var legacyPath in GetLegacyDatabaseCandidates())
        {
            if (string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(legacyPath))
            {
                continue;
            }

            var backupPath = Path.Combine(
                BackupsDirectory,
                $"time-controller_backup_{DateTime.Now:yyyyMMdd_HHmmss}_legacy.db");
            File.Copy(legacyPath, backupPath, overwrite: false);
            File.Copy(legacyPath, DatabasePath, overwrite: false);
            LogService.Info("Migrated legacy database from " + legacyPath);
            return;
        }
    }

    private static IEnumerable<string> GetLegacyDatabaseCandidates()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyAppName,
            "app_usage.db");
        yield return Path.Combine(AppContext.BaseDirectory, "app_usage.db");
    }
}
