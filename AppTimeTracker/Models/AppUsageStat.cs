namespace AppTimeTracker.Models;

public sealed class AppUsageStat
{
    public int AppId { get; set; }
    public int Rank { get; set; }
    public string DisplayName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int RangeDurationSeconds { get; set; }
    public int TodayDurationSeconds { get; set; }
    public int TotalDurationSeconds { get; set; }
    public string RangeDurationText => TimeFormatter.Format(RangeDurationSeconds);
    public string TodayDurationText => TimeFormatter.Format(TodayDurationSeconds);
    public string TotalDurationText => TimeFormatter.Format(TotalDurationSeconds);
}
