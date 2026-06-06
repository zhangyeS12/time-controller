using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class BackupService
{
    private const string ManifestFileName = "backup_manifest.json";
    private const string RestoreGuardFileName = "restore_guard_temp.db";
    private readonly DatabaseService databaseService;

    private static readonly BackupSlotDefinition[] Slots =
    [
        new("auto_latest", "\u81ea\u52a8\u5b58\u6863", "auto", "auto_latest.db", 0),
        new("auto_3_days", "3 \u5929\u5b58\u6863", "auto", "auto_3_days.db", 3),
        new("auto_7_days", "7 \u5929\u5b58\u6863", "auto", "auto_7_days.db", 7),
        new("auto_30_days", "30 \u5929\u5b58\u6863", "auto", "auto_30_days.db", 30),
        new("manual_slot_1", "\u624b\u52a8\u5b58\u6863 1", "manual", "manual_slot_1.db", null),
        new("manual_slot_2", "\u624b\u52a8\u5b58\u6863 2", "manual", "manual_slot_2.db", null),
        new("manual_slot_3", "\u624b\u52a8\u5b58\u6863 3", "manual", "manual_slot_3.db", null),
        new("manual_slot_4", "\u624b\u52a8\u5b58\u6863 4", "manual", "manual_slot_4.db", null)
    ];

    public BackupService(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<IReadOnlyList<BackupInfo>> GetBackupSlotsAsync()
    {
        AppPaths.EnsureDirectories();
        return await Task.Run(() =>
        {
            var manifest = LoadManifest();
            var result = Slots.Select(slot => CreateSlotInfo(slot, manifest)).ToList();
            SaveManifest(result);
            return (IReadOnlyList<BackupInfo>)result;
        });
    }

    public async Task<BackupInfo> SaveToSlotAsync(string slotId)
    {
        var slot = FindSlot(slotId);
        var targetPath = GetSlotPath(slot);

        await Task.Run(() =>
        {
            EnsureDatabaseExists();
            File.Copy(databaseService.DatabasePath, targetPath, overwrite: true);
        });

        var slots = await GetBackupSlotsAsync();
        LogService.Info("Backup slot saved: " + slot.SlotId);
        return slots.First(item => item.SlotId == slot.SlotId);
    }

    public async Task RestoreSlotAsync(string slotId)
    {
        var slot = FindSlot(slotId);
        var sourcePath = GetSlotPath(slot);
        EnsureBackupPath(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException("Backup slot is empty.");
        }

        var guardPath = Path.Combine(AppPaths.BackupsDirectory, RestoreGuardFileName);

        await Task.Run(() =>
        {
            if (File.Exists(databaseService.DatabasePath))
            {
                File.Copy(databaseService.DatabasePath, guardPath, overwrite: true);
            }

            try
            {
                File.Copy(sourcePath, databaseService.DatabasePath, overwrite: true);
                databaseService.Initialize();
                if (File.Exists(guardPath))
                {
                    File.Delete(guardPath);
                }
            }
            catch
            {
                if (File.Exists(guardPath))
                {
                    File.Copy(guardPath, databaseService.DatabasePath, overwrite: true);
                }

                throw;
            }
        });

        LogService.Info("Backup slot restored: " + slot.SlotId);
    }

    public async Task ClearSlotAsync(string slotId)
    {
        var slot = FindSlot(slotId);
        var path = GetSlotPath(slot);
        EnsureBackupPath(path);

        await Task.Run(() =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });

        SaveManifest((await GetBackupSlotsAsync()).ToList());
        LogService.Info("Backup slot cleared: " + slot.SlotId);
    }

    public async Task RunScheduledBackupsAsync()
    {
        var now = GetCurrentStatisticsTime();
        var slots = await GetBackupSlotsAsync();

        foreach (var slot in Slots.Where(item => item.IntervalDays is > 0))
        {
            var info = slots.First(item => item.SlotId == slot.SlotId);
            if (info.IsEmpty || info.LastBackupAt is null || (now.Date - info.LastBackupAt.Value.Date).TotalDays >= slot.IntervalDays)
            {
                await SaveToSlotAsync(slot.SlotId);
            }
        }
    }

    public async Task<int> GetLegacyBackupCountAsync()
    {
        AppPaths.EnsureDirectories();
        return await Task.Run(() => Directory.EnumerateFiles(AppPaths.BackupsDirectory, "time-controller_backup_*.db").Count());
    }

    public async Task CleanupLegacyBackupsAsync()
    {
        AppPaths.EnsureDirectories();
        await Task.Run(() =>
        {
            foreach (var path in Directory.EnumerateFiles(AppPaths.BackupsDirectory, "time-controller_backup_*.db"))
            {
                EnsureBackupPath(path);
                File.Delete(path);
                LogService.Info("Legacy backup deleted: " + path);
            }
        });
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

    public async Task<BackupInfo> CreateBackupAsync(string backupType)
    {
        var normalized = string.IsNullOrWhiteSpace(backupType) ? "auto" : backupType.Trim().ToLowerInvariant();
        var slotId = normalized switch
        {
            "manual" => "manual_slot_1",
            "exit" => "auto_latest",
            "auto" => "auto_latest",
            _ => "auto_latest"
        };

        return await SaveToSlotAsync(slotId);
    }

    public async Task<IReadOnlyList<BackupInfo>> GetBackupsAsync()
    {
        return await GetBackupSlotsAsync();
    }

    public async Task RestoreBackupAsync(BackupInfo backup)
    {
        await RestoreSlotAsync(backup.SlotId);
    }

    public async Task DeleteBackupAsync(BackupInfo backup)
    {
        await ClearSlotAsync(backup.SlotId);
    }

    public async Task CleanupOldBackupsAsync(int maxAutoBackups, int maxBeforeRestoreBackups = 10)
    {
        await Task.CompletedTask;
    }

    private static BackupInfo CreateSlotInfo(BackupSlotDefinition slot, Dictionary<string, ManifestItem> manifest)
    {
        var path = GetSlotPath(slot);
        var file = new FileInfo(path);
        manifest.TryGetValue(slot.SlotId, out var manifestItem);
        DateTime? lastBackupAt = file.Exists
            ? manifestItem?.LastBackupAt ?? file.LastWriteTime
            : null;

        return new BackupInfo
        {
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            SlotType = slot.SlotType,
            FileName = slot.FileName,
            FullPath = path,
            LastBackupAt = lastBackupAt,
            FileSizeBytes = file.Exists ? file.Length : 0,
            IsEmpty = !file.Exists
        };
    }

    private static Dictionary<string, ManifestItem> LoadManifest()
    {
        var path = GetManifestPath();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<ManifestItem>>(File.ReadAllText(path)) ?? [];
            return items.ToDictionary(item => item.SlotId, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Exception("Backup manifest load failed", ex);
            return [];
        }
    }

    private static void SaveManifest(IReadOnlyList<BackupInfo> slots)
    {
        var items = slots.Select(slot => new ManifestItem
        {
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            SlotType = slot.SlotType,
            FileName = slot.FileName,
            LastBackupAt = slot.LastBackupAt,
            LastBackupAtText = slot.LastBackupAtText,
            FileSizeBytes = slot.FileSizeBytes,
            FileSizeText = slot.FileSizeText,
            IsEmpty = slot.IsEmpty
        }).ToList();

        File.WriteAllText(GetManifestPath(), JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static BackupSlotDefinition FindSlot(string slotId)
    {
        return Slots.FirstOrDefault(item => string.Equals(item.SlotId, slotId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Unknown backup slot.");
    }

    private DateTime GetCurrentStatisticsTime()
    {
        var mode = databaseService.GetSetting("TimeZone.Mode", "system");
        var timeZoneId = databaseService.GetSetting("TimeZone.Id", TimeZoneInfo.Local.Id);
        if (mode != "fixed")
        {
            return DateTime.Now;
        }

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.Now;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.Now;
        }
    }

    private void EnsureDatabaseExists()
    {
        if (!File.Exists(databaseService.DatabasePath))
        {
            throw new FileNotFoundException("Database file not found.", databaseService.DatabasePath);
        }
    }

    private static string GetSlotPath(BackupSlotDefinition slot)
    {
        AppPaths.EnsureDirectories();
        return Path.Combine(AppPaths.BackupsDirectory, slot.FileName);
    }

    private static string GetManifestPath()
    {
        AppPaths.EnsureDirectories();
        return Path.Combine(AppPaths.BackupsDirectory, ManifestFileName);
    }

    private static void EnsureBackupPath(string path)
    {
        var backupRoot = Path.GetFullPath(AppPaths.BackupsDirectory);
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid backup path.");
        }
    }

    private sealed record BackupSlotDefinition(
        string SlotId,
        string SlotName,
        string SlotType,
        string FileName,
        int? IntervalDays);

    private sealed class ManifestItem
    {
        public string SlotId { get; set; } = "";
        public string SlotName { get; set; } = "";
        public string SlotType { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime? LastBackupAt { get; set; }
        public string LastBackupAtText { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public string FileSizeText { get; set; } = "";
        public bool IsEmpty { get; set; }
    }
}
