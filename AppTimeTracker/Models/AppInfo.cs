namespace AppTimeTracker.Models;

public sealed class AppInfo
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Category { get; set; }
    public bool IsIgnored { get; set; }

    public string NameForDisplay => string.IsNullOrWhiteSpace(DisplayName) ? ProcessName : DisplayName;
}
