namespace AppTimeTracker.Models;

public sealed class AppUsageMultiRangeStat
{
    public int Rank { get; set; }
    public string DisplayName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int TodayDurationSeconds { get; set; }
    public int Last7DaysDurationSeconds { get; set; }
    public int Last30DaysDurationSeconds { get; set; }
    public int TotalDurationSeconds { get; set; }
    public string TodayDurationText => TimeFormatter.Format(TodayDurationSeconds);
    public string Last7DaysDurationText => TimeFormatter.Format(Last7DaysDurationSeconds);
    public string Last30DaysDurationText => TimeFormatter.Format(Last30DaysDurationSeconds);
    public string TotalDurationText => TimeFormatter.Format(TotalDurationSeconds);
}
