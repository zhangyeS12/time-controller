namespace AppTimeTracker.Models;

public sealed class FocusDistraction
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
}
