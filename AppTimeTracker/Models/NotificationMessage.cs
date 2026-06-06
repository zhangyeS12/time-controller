namespace AppTimeTracker.Models;

public sealed class NotificationMessage
{
    public string Title { get; set; } = "time-controller";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "Info";
}
