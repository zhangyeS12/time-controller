namespace AppTimeTracker.Models;

public sealed class FocusRule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TargetType { get; set; } = "app";
    public string TargetValue { get; set; } = "";
    public string RuleType { get; set; } = "max";
    public int LimitSeconds { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool NotifyEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string TargetTypeText => TargetType == "category" ? "分类" : "应用";
    public string RuleTypeText => RuleType == "min" ? "至少使用" : "最多使用";
    public string LimitText => TimeFormatter.Format(LimitSeconds);
}
