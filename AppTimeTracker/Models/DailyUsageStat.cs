namespace AppTimeTracker.Models;

public sealed class DailyUsageStat
{
    public DateTime Date { get; set; }
    public int DurationSeconds { get; set; }
    public double Percent { get; set; }
    public string DateText => Date.ToString("MM-dd");
    public string DurationText => TimeFormatter.Format(DurationSeconds);
}
