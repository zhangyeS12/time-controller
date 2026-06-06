using System.Diagnostics;
using System.IO;
using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class BackupService
{
    private readonly DatabaseService databaseService;

    public BackupService(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<BackupInfo> CreateBackupAsync(string backupType)
    {
        AppPaths.EnsureDirectories();
        var type = string.IsNullOrWhiteSpace(backupType) ? "manual" : backupType.Trim();
        var fileName = $"time-controller_backup_{DateTime.Now:yyyyMMdd_HHmmss}_{type}.db";
        var path = Path.Combine(AppPaths.BackupsDirectory, fileName);

        await Task.Run(() =>
        {
            if (!File.Exists(databaseService.DatabasePath))
            {
                throw new FileNotFoundException("数据库文件不存在，无法创建备份。", databaseService.DatabasePath);
            }

            File.Copy(databaseService.DatabasePath, path, overwrite: false);
        });

        LogService.Info("Database backup created: " + path);
        return CreateInfo(path);
    }

    public async Task<IReadOnlyList<BackupInfo>> GetBackupsAsync()
    {
        AppPaths.EnsureDirectories();
        return await Task.Run(() =>
            Directory.EnumerateFiles(AppPaths.BackupsDirectory, "*.db")
                .Select(CreateInfo)
                .OrderByDescending(item => item.CreatedAt)
                .ToList());
    }

    public async Task RestoreBackupAsync(BackupInfo backup)
    {
        EnsureBackupPath(backup.FullPath);
        await CreateBackupAsync("before_restore");
        await databaseService.RestoreDatabaseAsync(backup.FullPath);
        LogService.Info("Database restored from backup: " + backup.FullPath);
    }

    public async Task DeleteBackupAsync(BackupInfo backup)
    {
        EnsureBackupPath(backup.FullPath);
        await Task.Run(() =>
        {
            if (File.Exists(backup.FullPath))
            {
                File.Delete(backup.FullPath);
            }
        });
        LogService.Info("Backup deleted: " + backup.FullPath);
    }

    public async Task CleanupOldBackupsAsync(int maxAutoBackups, int maxBeforeRestoreBackups = 10)
    {
        var backups = await GetBackupsAsync();
        await DeleteOverflowAsync(backups.Where(item => item.BackupType is "auto" or "exit"), Math.Max(1, maxAutoBackups));
        await DeleteOverflowAsync(backups.Where(item => item.BackupType == "before_restore"), Math.Max(1, maxBeforeRestoreBackups));
    }

    public void OpenBackupFolder()
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.BackupsDirectory,
            UseShellExecute = true
        });
    }

    private static async Task DeleteOverflowAsync(IEnumerable<BackupInfo> source, int maxCount)
    {
        var overflow = source
            .OrderByDescending(item => item.CreatedAt)
            .Skip(maxCount)
            .ToList();

        foreach (var item in overflow)
        {
            EnsureBackupPath(item.FullPath);
            await Task.Run(() => File.Delete(item.FullPath));
            LogService.Info("Old backup cleaned: " + item.FullPath);
        }
    }

    private static BackupInfo CreateInfo(string path)
    {
        var file = new FileInfo(path);
        return new BackupInfo
        {
            FileName = file.Name,
            FullPath = file.FullName,
            BackupType = ParseType(file.Name),
            CreatedAt = file.CreationTime,
            FileSizeBytes = file.Exists ? file.Length : 0
        };
    }

    private static string ParseType(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var marker = "time-controller_backup_";
        if (!name.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return "legacy";
        }

        var parts = name.Split('_');
        return parts.Length >= 5 ? string.Join('_', parts.Skip(4)) : "manual";
    }

    private static void EnsureBackupPath(string path)
    {
        var backupRoot = Path.GetFullPath(AppPaths.BackupsDirectory);
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能操作备份目录中的文件。");
        }
    }
}
