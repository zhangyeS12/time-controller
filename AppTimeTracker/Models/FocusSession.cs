namespace AppTimeTracker.Models;

public sealed class FocusSession
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TargetType { get; set; } = "app";
    public string TargetValue { get; set; } = "";
    public int PlannedSeconds { get; set; }
    public int ActualSeconds { get; set; }
    public int DistractionCount { get; set; }
    public string Status { get; set; } = "running";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }

    public string PlannedText => TimeFormatter.Format(PlannedSeconds);
    public string ActualText => TimeFormatter.Format(ActualSeconds);
}
