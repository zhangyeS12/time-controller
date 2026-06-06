namespace AppTimeTracker.Models;

public sealed class BackupInfo
{
    public string FileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string BackupType { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public long FileSizeBytes { get; init; }

    public string TypeText => BackupType switch
    {
        "manual" => "手动备份",
        "auto" => "自动备份",
        "exit" => "退出备份",
        "before_restore" => "恢复前备份",
        "legacy" => "旧数据迁移",
        _ => "备份"
    };

    public string CreatedAtText => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string FileSizeText => FileSizeBytes < 1024 * 1024
        ? $"{Math.Max(1, FileSizeBytes / 1024)} KB"
        : $"{FileSizeBytes / 1024.0 / 1024.0:0.##} MB";
}
