namespace AppTimeTracker.Models;

public sealed class ReportSummary
{
    public int TotalDurationSeconds { get; set; }
    public int AverageDailyDurationSeconds { get; set; }
    public string MostUsedApp { get; set; } = "暂无";
    public string MostUsedCategory { get; set; } = "暂无";
    public int AppCount { get; set; }
    public int RuleNotificationCount { get; set; }

    public string TotalDurationText => TimeFormatter.Format(TotalDurationSeconds);
    public string AverageDailyDurationText => TimeFormatter.Format(AverageDailyDurationSeconds);
}
