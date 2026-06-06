namespace AppTimeTracker.Models;

public sealed class BackupInfo
{
    public string SlotId { get; init; } = "";
    public string SlotName { get; init; } = "";
    public string SlotType { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public DateTime? LastBackupAt { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsEmpty { get; set; }

    public string BackupType => SlotType;
    public DateTime CreatedAt => LastBackupAt ?? DateTime.MinValue;

    public string TypeText => SlotType switch
    {
        "auto" => "自动备份",
        "manual" => "手动备份",
        _ => "备份"
    };

    public string LastBackupAtText => IsEmpty || LastBackupAt is null
        ? ""
        : LastBackupAt.Value.ToString("yyyy-MM-dd HH:mm:ss");

    public string CreatedAtText => IsEmpty ? "" : LastBackupAtText;

    public string FileSizeText => IsEmpty
        ? "-"
        : FileSizeBytes < 1024 * 1024
            ? $"{Math.Max(1, FileSizeBytes / 1024)} KB"
            : $"{FileSizeBytes / 1024.0 / 1024.0:0.##} MB";
}
