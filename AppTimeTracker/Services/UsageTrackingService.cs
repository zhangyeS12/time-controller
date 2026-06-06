using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class UsageTrackingService
{
    private const string SelfProcessName = "AppTimeTracker.exe";
    private readonly ForegroundWindowService foregroundWindowService;
    private readonly IdleDetector idleDetector;
    private readonly DatabaseService databaseService;
    private int? currentSessionId;
    private TrackedWindowInfo? currentWindow;
    private DateTime? currentSessionStart;
    private bool wasIdle;
    private bool wasIgnored;
    private bool isManuallyPaused;

    public UsageTrackingService(
        ForegroundWindowService foregroundWindowService,
        IdleDetector idleDetector,
        DatabaseService databaseService)
    {
        this.foregroundWindowService = foregroundWindowService;
        this.idleDetector = idleDetector;
        this.databaseService = databaseService;
    }

    public string CurrentAppName => currentWindow?.DisplayName ?? "未统计";
    public string CurrentProcessName => currentWindow?.ProcessName ?? "";
    public string? CurrentWindowTitle => currentWindow?.WindowTitle;
    public DateTime? CurrentSessionStart => currentSessionStart;
    public int CurrentDurationSeconds => currentSessionStart is null
        ? 0
        : Math.Max(0, (int)(DateTime.Now - currentSessionStart.Value).TotalSeconds);
    public bool IsPausedByIdle => wasIdle;
    public bool IsPausedByIgnoredApp => wasIgnored;
    public bool IsManuallyPaused => isManuallyPaused;

    public void Tick(TimeSpan idleThreshold, bool recordWindowTitle)
    {
        if (isManuallyPaused)
        {
            EndCurrentSession(DateTime.Now);
            return;
        }

        var isIdle = idleDetector.IsIdle(idleThreshold);
        if (isIdle)
        {
            if (!wasIdle)
            {
                EndCurrentSession(DateTime.Now);
            }

            wasIdle = true;
            return;
        }

        wasIdle = false;
        wasIgnored = false;

        var foregroundWindow = foregroundWindowService.GetCurrentForegroundWindow(recordWindowTitle);
        if (foregroundWindow is null)
        {
            EndCurrentSession(DateTime.Now);
            return;
        }

        if (IsSelfProcess(foregroundWindow.ProcessName)
            || databaseService.IsAppIgnoredAsync(foregroundWindow.ProcessName).GetAwaiter().GetResult())
        {
            EndCurrentSession(DateTime.Now);
            wasIgnored = true;
            return;
        }

        if (currentWindow is not null
            && string.Equals(currentWindow.ProcessName, foregroundWindow.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EndCurrentSession(DateTime.Now);
        StartSession(foregroundWindow, DateTime.Now);
    }

    public void Stop()
    {
        EndCurrentSession(DateTime.Now);
    }

    public void Pause()
    {
        isManuallyPaused = true;
        wasIdle = false;
        wasIgnored = false;
        EndCurrentSession(DateTime.Now);
    }

    public void Resume()
    {
        isManuallyPaused = false;
        wasIdle = false;
        wasIgnored = false;
    }

    public void ResetAfterDataClear()
    {
        currentSessionId = null;
        currentWindow = null;
        currentSessionStart = null;
        wasIgnored = false;
    }

    private void StartSession(TrackedWindowInfo windowInfo, DateTime startTime)
    {
        var appId = databaseService.GetOrCreateApp(windowInfo.ProcessName, windowInfo.DisplayName);
        currentSessionId = databaseService.StartSession(appId, windowInfo.WindowTitle, startTime);
        currentWindow = windowInfo;
        currentSessionStart = startTime;
    }

    private void EndCurrentSession(DateTime endTime)
    {
        if (currentSessionId is not null)
        {
            databaseService.EndSession(currentSessionId.Value, endTime);
        }

        currentSessionId = null;
        currentWindow = null;
        currentSessionStart = null;
    }

    private static bool IsSelfProcess(string processName)
    {
        return string.Equals(processName, SelfProcessName, StringComparison.OrdinalIgnoreCase);
    }
}
