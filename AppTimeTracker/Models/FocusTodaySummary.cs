namespace AppTimeTracker.Models;

public sealed class FocusTodaySummary
{
    public int CompletedCount { get; set; }
    public int TotalFocusSeconds { get; set; }
    public int TotalDistractionCount { get; set; }

    public string TotalFocusText => TimeFormatter.Format(TotalFocusSeconds);
}
