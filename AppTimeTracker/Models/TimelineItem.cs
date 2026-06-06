namespace AppTimeTracker.Models;

public sealed class TimelineItem
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string AppName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string? DisplayTimeRange { get; set; }

    public string TimeRange => string.IsNullOrWhiteSpace(DisplayTimeRange)
        ? $"{StartTime:HH:mm} - {(EndTime?.ToString("HH:mm") ?? "鐜板湪")}"
        : DisplayTimeRange;

    public string DurationText => TimeFormatter.Format(DurationSeconds);
    public string Description => $"{TimeRange}  {AppName}";
}
