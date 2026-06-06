namespace AppTimeTracker.Models;

public sealed class TrackedWindowInfo
{
    public string ProcessName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? WindowTitle { get; init; }
}
