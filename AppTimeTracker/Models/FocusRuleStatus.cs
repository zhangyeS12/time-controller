using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppTimeTracker.Models;

public sealed class FocusRuleStatus : INotifyPropertyChanged
{
    private bool isEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TargetType { get; set; } = "app";
    public string TargetValue { get; set; } = "";
    public string TargetDisplayName { get; set; } = "";
    public string RuleType { get; set; } = "max";
    public int LimitSeconds { get; set; }
    public bool NotifyEnabled { get; set; }
    public int TodayUsageSeconds { get; set; }
    public bool HasNotifiedToday { get; set; }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value) return;
            isEnabled = value;
            OnPropertyChanged();
        }
    }

    public string TargetTypeText => TargetType == "category" ? "分类" : "应用";
    public string RuleTypeText => RuleType == "min" ? "至少使用" : "最多使用";
    public string LimitText => TimeFormatter.Format(LimitSeconds);
    public string TodayUsageText => TimeFormatter.Format(TodayUsageSeconds);
    public string ProgressText => $"{Math.Min(999, ProgressPercent):0}%";
    public double ProgressPercent => LimitSeconds <= 0 ? 0 : Math.Min(100, TodayUsageSeconds * 100.0 / LimitSeconds);
    public bool IsOverLimit => RuleType == "max" && TodayUsageSeconds >= LimitSeconds;
    public bool IsCompleted => RuleType == "min" ? TodayUsageSeconds >= LimitSeconds : TodayUsageSeconds < LimitSeconds;
    public string StateText => IsEnabled
        ? IsOverLimit ? "已超时" : IsCompleted ? "已完成" : "进行中"
        : "已停用";
    public string AccentBrushKey => IsOverLimit ? "BrushDanger" : "BrushPrimary";

    public static FocusRuleStatus FromRule(FocusRule rule, string targetDisplayName, int todayUsageSeconds, bool hasNotifiedToday)
    {
        return new FocusRuleStatus
        {
            Id = rule.Id,
            Name = rule.Name,
            TargetType = rule.TargetType,
            TargetValue = rule.TargetValue,
            TargetDisplayName = targetDisplayName,
            RuleType = rule.RuleType,
            LimitSeconds = rule.LimitSeconds,
            IsEnabled = rule.IsEnabled,
            NotifyEnabled = rule.NotifyEnabled,
            TodayUsageSeconds = todayUsageSeconds,
            HasNotifiedToday = hasNotifiedToday
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
