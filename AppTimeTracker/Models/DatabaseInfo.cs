namespace AppTimeTracker.Models;

public sealed class DatabaseInfo
{
    public string DatabasePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int UsageSessionCount { get; set; }
    public int AppCount { get; set; }
    public DateTime? EarliestRecordTime { get; set; }
    public DateTime? LatestRecordTime { get; set; }

    public string FileSizeText => FileSizeBytes < 1024 * 1024
        ? $"{Math.Max(1, FileSizeBytes / 1024)} KB"
        : $"{FileSizeBytes / 1024.0 / 1024.0:0.##} MB";
    public string EarliestRecordText => EarliestRecordTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无";
    public string LatestRecordText => LatestRecordTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无";
}
