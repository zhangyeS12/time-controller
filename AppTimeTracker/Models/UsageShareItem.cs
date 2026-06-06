namespace AppTimeTracker.Models;

public sealed class UsageShareItem
{
    public string DisplayName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Category { get; set; } = "";
    public int DurationSeconds { get; set; }
    public double Percentage { get; set; }
    public string DurationText => TimeFormatter.Format(DurationSeconds);
    public string PercentageText => $"{Percentage:0.#}%";
}
