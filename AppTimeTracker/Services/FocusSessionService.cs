using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class FocusSessionService
{
    private readonly DatabaseService databaseService;
    private readonly NotificationService notificationService;
    private FocusSession? currentSession;
    private DateTime? distractionCandidateStart;
    private DateTime? activeDistractionStart;
    private int? activeDistractionId;
    private string activeDistractionProcess = "";
    private string activeDistractionDisplay = "";

    public FocusSessionService(DatabaseService databaseService, NotificationService notificationService)
    {
        this.databaseService = databaseService;
        this.notificationService = notificationService;
    }

    public FocusSession? CurrentSession => currentSession;
    public bool AllowBrowser { get; set; } = true;
    public bool AllowSystemTools { get; set; } = true;
    public bool DistractionNotifyEnabled { get; set; } = true;
    public bool IsRunning => currentSession?.Status == "running";
    public bool IsPaused => currentSession?.Status == "paused";
    public int RemainingSeconds => currentSession is null ? 0 : Math.Max(0, currentSession.PlannedSeconds - currentSession.ActualSeconds);
    public int ActualSeconds => currentSession?.ActualSeconds ?? 0;
    public int DistractionCount => currentSession?.DistractionCount ?? 0;
    public string StatusText => currentSession?.Status switch
    {
        "running" => "专注中",
        "paused" => "已暂停",
        "completed" => "已完成",
        "abandoned" => "已放弃",
        _ => "未开始"
    };

    public async Task StartAsync(string name, string targetType, string targetValue, int plannedSeconds, bool allowBrowser, bool allowSystemTools, bool notifyEnabled)
    {
        if (currentSession is { Status: "running" or "paused" })
        {
            await AbandonAsync();
        }

        AllowBrowser = allowBrowser;
        AllowSystemTools = allowSystemTools;
        DistractionNotifyEnabled = notifyEnabled;
        currentSession = new FocusSession
        {
            Name = string.IsNullOrWhiteSpace(name) ? "专注会话" : name.Trim(),
            TargetType = targetType,
            TargetValue = targetValue,
            PlannedSeconds = Math.Max(60, plannedSeconds),
            Status = "running",
            StartedAt = DateTime.Now
        };
        currentSession.Id = await databaseService.CreateFocusSessionAsync(currentSession);
        ResetDistractionState();
    }

    public async Task PauseAsync()
    {
        if (currentSession is null || currentSession.Status != "running") return;
        await EndActiveDistractionAsync();
        currentSession.Status = "paused";
        await databaseService.UpdateFocusSessionAsync(currentSession);
    }

    public async Task ResumeAsync()
    {
        if (currentSession is null || currentSession.Status != "paused") return;
        currentSession.Status = "running";
        await databaseService.UpdateFocusSessionAsync(currentSession);
    }

    public async Task EndAsync()
    {
        if (currentSession is null) return;
        await EndActiveDistractionAsync();
        currentSession.Status = "completed";
        currentSession.EndedAt = DateTime.Now;
        await databaseService.CompleteFocusSessionAsync(currentSession.Id, currentSession.ActualSeconds, currentSession.DistractionCount);
        notificationService.FocusCompleted($"{currentSession.Name} 已完成，累计专注 {TimeFormatter.Format(currentSession.ActualSeconds)}。");
    }

    public async Task AbandonAsync()
    {
        if (currentSession is null || currentSession.Status is "completed" or "abandoned") return;
        await EndActiveDistractionAsync();
        currentSession.Status = "abandoned";
        currentSession.EndedAt = DateTime.Now;
        await databaseService.AbandonFocusSessionAsync(currentSession.Id, currentSession.ActualSeconds, currentSession.DistractionCount);
    }

    public async Task TickAsync(string processName, string displayName)
    {
        if (currentSession is null || currentSession.Status != "running")
        {
            return;
        }

        currentSession.ActualSeconds++;
        if (currentSession.ActualSeconds >= currentSession.PlannedSeconds)
        {
            await EndAsync();
            return;
        }

        var allowed = await IsAllowedAsync(processName);
        if (allowed)
        {
            distractionCandidateStart = null;
            await EndActiveDistractionAsync();
            return;
        }

        distractionCandidateStart ??= DateTime.Now;
        if (activeDistractionId is null && (DateTime.Now - distractionCandidateStart.Value).TotalSeconds >= 10)
        {
            currentSession.DistractionCount++;
            activeDistractionStart = distractionCandidateStart.Value;
            activeDistractionProcess = processName;
            activeDistractionDisplay = displayName;
            activeDistractionId = await databaseService.AddFocusDistractionAsync(new FocusDistraction
            {
                SessionId = currentSession.Id,
                ProcessName = processName,
                DisplayName = displayName,
                StartedAt = activeDistractionStart.Value
            });

            if (DistractionNotifyEnabled)
            {
                notificationService.Distraction($"你已经离开专注目标超过 10 秒，当前应用：{displayName}。");
            }
        }
    }

    private async Task<bool> IsAllowedAsync(string processName)
    {
        if (currentSession is null || string.IsNullOrWhiteSpace(processName))
        {
            return true;
        }

        var app = await databaseService.GetAppInfoByProcessNameAsync(processName);
        var category = app?.Category ?? "";
        if (currentSession.TargetType == "app" && string.Equals(currentSession.TargetValue, processName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (currentSession.TargetType == "category" && string.Equals(currentSession.TargetValue, category, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AllowBrowser && string.Equals(category, "浏览器", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AllowSystemTools && string.Equals(category, "系统工具", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task EndActiveDistractionAsync()
    {
        if (activeDistractionId is not null && activeDistractionStart is not null)
        {
            var duration = Math.Max(0, (int)(DateTime.Now - activeDistractionStart.Value).TotalSeconds);
            await databaseService.EndFocusDistractionAsync(activeDistractionId.Value, DateTime.Now, duration);
        }

        ResetDistractionState();
    }

    private void ResetDistractionState()
    {
        distractionCandidateStart = null;
        activeDistractionStart = null;
        activeDistractionId = null;
        activeDistractionProcess = "";
        activeDistractionDisplay = "";
    }
}
