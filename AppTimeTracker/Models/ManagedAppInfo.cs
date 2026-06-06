namespace AppTimeTracker.Models;

public sealed class ManagedAppInfo
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "其他";
    public bool IsIgnored { get; set; }
    public int TodayDurationSeconds { get; set; }
    public int Last7DaysDurationSeconds { get; set; }
    public int Last30DaysDurationSeconds { get; set; }
    public int TotalDurationSeconds { get; set; }
}
