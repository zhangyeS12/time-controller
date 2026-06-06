namespace AppTimeTracker.Models;

public sealed class UsageSession
{
    public int Id { get; set; }
    public int AppId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? WindowTitle { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
}
