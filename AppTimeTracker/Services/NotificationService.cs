using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class NotificationService
{
    public event EventHandler<NotificationMessage>? NotificationRaised;

    public void Info(string title, string message) => Raise(title, message, "Info");
    public void Warning(string title, string message) => Raise(title, message, "Warning");
    public void Success(string title, string message) => Raise(title, message, "Success");
    public void Error(string title, string message) => Raise(title, message, "Error");
    public void FocusCompleted(string message) => Success("专注完成", message);
    public void Distraction(string message) => Warning("分心提醒", message);

    private void Raise(string title, string message, string type)
    {
        NotificationRaised?.Invoke(this, new NotificationMessage
        {
            Title = title,
            Message = message,
            Type = type
        });
    }
}
