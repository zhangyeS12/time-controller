namespace AppTimeTracker.Models;

public static class TimeFormatter
{
    public static string Format(int seconds)
    {
        var total = Math.Max(0, seconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;

        if (hours > 0)
        {
            return $"{hours}小时 {minutes}分钟";
        }

        if (minutes > 0)
        {
            return $"{minutes}分钟 {secs}秒";
        }

        return $"{secs}秒";
    }
}
