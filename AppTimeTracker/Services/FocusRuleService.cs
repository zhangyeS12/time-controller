using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class FocusRuleService
{
    private readonly DatabaseService databaseService;

    public FocusRuleService(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public event EventHandler<FocusRuleNotificationEventArgs>? RuleNotificationRaised;

    public async Task<List<FocusRuleStatus>> GetRuleStatusesAsync(string liveProcessName, int liveDurationSeconds)
    {
        var rules = await databaseService.GetFocusRulesAsync();
        var apps = await databaseService.GetAllAppsWithStatsAsync();
        var appByProcess = apps.ToDictionary(app => app.ProcessName, StringComparer.OrdinalIgnoreCase);
        var result = new List<FocusRuleStatus>();

        foreach (var rule in rules)
        {
            var todayUsage = await GetRuleUsageSecondsAsync(rule, liveProcessName, liveDurationSeconds, appByProcess);
            var targetDisplayName = GetTargetDisplayName(rule, appByProcess);
            var hasNotifiedToday = await databaseService.HasRuleNotifiedTodayAsync(rule.Id);
            var status = FocusRuleStatus.FromRule(rule, targetDisplayName, todayUsage, hasNotifiedToday);
            result.Add(status);

            if (ShouldNotify(rule, todayUsage, hasNotifiedToday))
            {
                await databaseService.MarkRuleNotifiedTodayAsync(rule.Id);
                status.HasNotifiedToday = true;
                RuleNotificationRaised?.Invoke(this, new FocusRuleNotificationEventArgs(
                    rule,
                    targetDisplayName,
                    todayUsage));
            }
        }

        return result;
    }

    private async Task<int> GetRuleUsageSecondsAsync(
        FocusRule rule,
        string liveProcessName,
        int liveDurationSeconds,
        IReadOnlyDictionary<string, ManagedAppInfo> appByProcess)
    {
        var seconds = rule.TargetType == "category"
            ? await databaseService.GetTodayUsageByCategoryAsync(rule.TargetValue)
            : await databaseService.GetTodayUsageByProcessNameAsync(rule.TargetValue);

        if (liveDurationSeconds <= 0 || string.IsNullOrWhiteSpace(liveProcessName))
        {
            return seconds;
        }

        if (rule.TargetType == "app"
            && string.Equals(rule.TargetValue, liveProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return seconds + liveDurationSeconds;
        }

        if (rule.TargetType == "category"
            && appByProcess.TryGetValue(liveProcessName, out var liveApp)
            && string.Equals(liveApp.Category, rule.TargetValue, StringComparison.OrdinalIgnoreCase))
        {
            return seconds + liveDurationSeconds;
        }

        return seconds;
    }

    private static string GetTargetDisplayName(FocusRule rule, IReadOnlyDictionary<string, ManagedAppInfo> appByProcess)
    {
        if (rule.TargetType == "category")
        {
            return rule.TargetValue;
        }

        return appByProcess.TryGetValue(rule.TargetValue, out var app)
            ? app.DisplayName
            : rule.TargetValue;
    }

    private static bool ShouldNotify(FocusRule rule, int todayUsageSeconds, bool hasNotifiedToday)
    {
        return rule.IsEnabled
            && rule.NotifyEnabled
            && !hasNotifiedToday
            && rule.RuleType == "max"
            && todayUsageSeconds >= rule.LimitSeconds;
    }
}

public sealed class FocusRuleNotificationEventArgs : EventArgs
{
    public FocusRuleNotificationEventArgs(FocusRule rule, string targetDisplayName, int todayUsageSeconds)
    {
        Rule = rule;
        TargetDisplayName = targetDisplayName;
        TodayUsageSeconds = todayUsageSeconds;
    }

    public FocusRule Rule { get; }
    public string TargetDisplayName { get; }
    public int TodayUsageSeconds { get; }
    public string Message =>
        $"{TargetDisplayName} 今日已使用 {TimeFormatter.Format(TodayUsageSeconds)}，超过你设置的 {TimeFormatter.Format(Rule.LimitSeconds)} 限制。";
}
