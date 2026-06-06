namespace AppTimeTracker.Services;

public sealed class TimeZoneService
{
    private readonly DatabaseService databaseService;
    private string mode = "system";
    private string timeZoneId = TimeZoneInfo.Local.Id;

    public TimeZoneService(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public Task LoadSettingsAsync()
    {
        mode = databaseService.GetSetting("TimeZone.Mode", "system");
        timeZoneId = databaseService.GetSetting("TimeZone.Id", TimeZoneInfo.Local.Id);
        if (mode != "fixed")
        {
            mode = "system";
            timeZoneId = TimeZoneInfo.Local.Id;
        }

        return Task.CompletedTask;
    }

    public Task SaveTimeZoneAsync(string selectedMode, string selectedTimeZoneId)
    {
        mode = selectedMode == "fixed" ? "fixed" : "system";
        timeZoneId = string.IsNullOrWhiteSpace(selectedTimeZoneId) ? TimeZoneInfo.Local.Id : selectedTimeZoneId;
        databaseService.SetSetting("TimeZone.Mode", mode);
        databaseService.SetSetting("TimeZone.Id", timeZoneId);
        LogService.Info($"Time zone setting changed: mode={mode}, id={timeZoneId}");
        return Task.CompletedTask;
    }

    public TimeZoneInfo GetCurrentTimeZone()
    {
        if (mode == "system")
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    public string GetCurrentTimeZoneDisplayName()
    {
        var zone = GetCurrentTimeZone();
        return $"{zone.DisplayName} ({zone.Id})";
    }

    public DateTime GetNowInSelectedTimeZone()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetCurrentTimeZone());
    }

    public DateTime ConvertUtcToDisplayTime(DateTime utcTime)
    {
        var utc = utcTime.Kind == DateTimeKind.Utc ? utcTime : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, GetCurrentTimeZone());
    }

    public DateTime ConvertStoredTimeToDisplayTime(DateTime storedTime)
    {
        if (storedTime.Kind == DateTimeKind.Utc)
        {
            return ConvertUtcToDisplayTime(storedTime);
        }

        var localStoredTime = DateTime.SpecifyKind(storedTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTime(localStoredTime, TimeZoneInfo.Local, GetCurrentTimeZone());
    }

    public string FormatDisplayTime(DateTime storedTime)
    {
        return ConvertStoredTimeToDisplayTime(storedTime).ToString("HH:mm");
    }

    public DateTime ConvertDisplayTimeToUtc(DateTime displayTime)
    {
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(displayTime, DateTimeKind.Unspecified), GetCurrentTimeZone());
    }

    public (DateTime StartUtc, DateTime EndUtc) GetTodayRangeUtc()
    {
        var today = GetNowInSelectedTimeZone().Date;
        return ToUtcRange(today, today.AddDays(1));
    }

    public (DateTime StartUtc, DateTime EndUtc) GetLast7DaysRangeUtc()
    {
        var today = GetNowInSelectedTimeZone().Date;
        return ToUtcRange(today.AddDays(-6), today.AddDays(1));
    }

    public (DateTime StartUtc, DateTime EndUtc) GetLast30DaysRangeUtc()
    {
        var today = GetNowInSelectedTimeZone().Date;
        return ToUtcRange(today.AddDays(-29), today.AddDays(1));
    }

    public (DateTime StartUtc, DateTime EndUtc) GetThisWeekRangeUtc()
    {
        var today = GetNowInSelectedTimeZone().Date;
        var diff = ((int)today.DayOfWeek + 6) % 7;
        var weekStart = today.AddDays(-diff);
        return ToUtcRange(weekStart, weekStart.AddDays(7));
    }

    public (DateTime StartUtc, DateTime EndUtc) GetLastWeekRangeUtc()
    {
        var current = GetThisWeekRangeUtc();
        return (current.StartUtc.AddDays(-7), current.StartUtc);
    }

    public (DateTime StartUtc, DateTime EndUtc) GetThisMonthRangeUtc()
    {
        var today = GetNowInSelectedTimeZone().Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return ToUtcRange(monthStart, monthStart.AddMonths(1));
    }

    public (DateTime StartUtc, DateTime EndUtc) GetLastMonthRangeUtc()
    {
        var today = GetNowInSelectedTimeZone().Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return ToUtcRange(monthStart.AddMonths(-1), monthStart);
    }

    public string GetSelectedZoneDateString(DateTime utcNow)
    {
        return ConvertUtcToDisplayTime(utcNow).ToString("yyyy-MM-dd");
    }

    private (DateTime StartUtc, DateTime EndUtc) ToUtcRange(DateTime displayStart, DateTime displayEnd)
    {
        return (ConvertDisplayTimeToUtc(displayStart), ConvertDisplayTimeToUtc(displayEnd));
    }
}
