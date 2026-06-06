using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AppTimeTracker.Models;
using AppTimeTracker.Services;
using Microsoft.Win32;

namespace AppTimeTracker.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DatabaseService databaseService;
    private readonly UsageTrackingService trackingService;
    private readonly CsvExportService csvExportService;
    private readonly FocusRuleService focusRuleService;
    private readonly NotificationService notificationService;
    private readonly FocusSessionService focusSessionService;
    private readonly BackupService backupService;
    private readonly TimeZoneService timeZoneService;
    private readonly DispatcherTimer timer;
    private HistoryRange currentHistoryRange = HistoryRange.Today;
    private ReportRange currentReportRange = ReportRange.ThisWeek;
    private DateTime nextFocusRuleCheck = DateTime.MinValue;
    private DateTime nextTodayRefresh = DateTime.MinValue;
    private int editingFocusRuleId;
    private bool isLoadingManagedApps;
    private bool isLoadingFocusRules;
    private bool isDataOperationRunning;
    private int idleThresholdSeconds = 60;
    private bool recordWindowTitle = true;
    private bool autoStart;
    private bool startMinimizedToTray;
    private bool closeToTray = true;
    private bool manualPaused;
    private bool autoDailyBackupEnabled;
    private bool backupOnExitEnabled;
    private int maxAutoBackups = 30;
    private string timeZoneMode = "system";
    private string selectedTimeZoneId = TimeZoneInfo.Local.Id;
    private string todayTotalTime = "0秒";
    private string todayTopApp = "暂无";
    private string currentApp = "未统计";
    private string currentDuration = "0秒";
    private string statusText = "正在统计";
    private string historyRangeTitle = "今天";
    private string historyTotalTime = "0秒";
    private string historyTopApp = "暂无";
    private string historyAverageDaily = "0秒";
    private string historyAppCount = "0";
    private string historyTrendTitle = "每日趋势";
    private string reportRangeTitle = "本周";
    private string reportTotalTime = "0秒";
    private string reportAverageDaily = "0秒";
    private string reportTopApp = "暂无";
    private string reportTopCategory = "暂无";
    private string reportAppCount = "0";
    private string reportRuleNotificationCount = "0";
    private string reportSummaryText = "暂无足够数据生成报告。";
    private string dataStatusText = "就绪";
    private string dataHealthCheckResult = "尚未检查";
    private string databaseFileSize = "-";
    private string databaseSessionCount = "0";
    private string databaseAppCount = "0";
    private string databaseEarliestRecord = "暂无";
    private string databaseLatestRecord = "暂无";
    private string focusSessionName = "25 分钟专注";
    private string focusSessionTargetType = "app";
    private string focusSessionAppProcessName = "";
    private string focusSessionCategory = "编程";
    private int focusSessionMinutes = 25;
    private bool focusSessionAllowBrowser = true;
    private bool focusSessionAllowSystemTools = true;
    private bool focusSessionNotifyEnabled = true;
    private string focusSessionStatus = "未开始";
    private string focusSessionRemaining = "0秒";
    private string focusSessionActual = "0秒";
    private string focusSessionDistractions = "0";
    private string focusSessionCurrentName = "暂无";
    private string todayFocusCompletedCount = "0";
    private string todayFocusTotalTime = "0秒";
    private string todayFocusDistractionCount = "0";
    private string reportFocusCompletedCount = "0";
    private string reportFocusTotalTime = "0秒";
    private string reportFocusAverageTime = "0秒";
    private string reportFocusDistractionCount = "0";
    private string focusNotificationCount = "0";
    private string focusOverLimitCount = "0";
    private string focusCompletedCount = "0";
    private string focusTodayState = "正常";
    private string focusRuleName = "";
    private string focusTargetType = "app";
    private string focusAppProcessName = "";
    private string focusCategory = "编程";
    private string focusRuleType = "max";
    private int focusLimitMinutes = 60;
    private bool focusRuleEnabled = true;
    private bool focusNotifyEnabled = true;
    private string focusFormTitle = "新增规则";

    public MainViewModel()
    {
        databaseService = new DatabaseService();
        trackingService = new UsageTrackingService(new ForegroundWindowService(), new IdleDetector(), databaseService);
        csvExportService = new CsvExportService();
        notificationService = new NotificationService();
        focusRuleService = new FocusRuleService(databaseService);
        focusSessionService = new FocusSessionService(databaseService, notificationService);
        backupService = new BackupService(databaseService);
        timeZoneService = new TimeZoneService(databaseService);
        notificationService.NotificationRaised += NotificationService_NotificationRaised;
        focusRuleService.RuleNotificationRaised += FocusRuleService_RuleNotificationRaised;

        CategoryOptions = new ObservableCollection<string>
        {
            "编程", "学习", "浏览器", "办公", "社交", "娱乐", "系统工具", "其他"
        };
        ManagedApps.CollectionChanged += ManagedApps_CollectionChanged;
        LoadSettings();

        ClearTodayCommand = new RelayCommand(_ => ClearTodayData());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        SetHistoryRangeCommand = new RelayCommand(value => SetHistoryRange(value?.ToString() ?? "Today"));
        PauseTrackingCommand = new RelayCommand(_ => ManualPaused = true);
        ResumeTrackingCommand = new RelayCommand(_ => ManualPaused = false);
        HideToTrayCommand = new RelayCommand(_ => RequestHideToTray?.Invoke(this, EventArgs.Empty));
        SaveFocusRuleCommand = new RelayCommand(_ => SaveFocusRule());
        ResetFocusRuleFormCommand = new RelayCommand(_ => ResetFocusRuleForm());
        EditFocusRuleCommand = new RelayCommand(value => EditFocusRule(value as FocusRuleStatus));
        DeleteFocusRuleCommand = new RelayCommand(value => DeleteFocusRule(value as FocusRuleStatus));
        SetReportRangeCommand = new RelayCommand(value => SetReportRange(value?.ToString() ?? "ThisWeek"));
        RefreshDatabaseInfoCommand = new RelayCommand(async _ => await RefreshDatabaseInfoAsync());
        ExportJsonCommand = new RelayCommand(async _ => await ExportJsonAsync());
        ExportReportTextCommand = new RelayCommand(async _ => await ExportReportTextAsync());
        BackupDatabaseCommand = new RelayCommand(async _ => await BackupDatabaseAsync());
        RestoreDatabaseCommand = new RelayCommand(async _ => await RestoreDatabaseAsync());
        CreateBackupCommand = new RelayCommand(async _ => await CreateManualBackupAsync());
        RestoreBackupCommand = new RelayCommand(async value => await RestoreBackupAsync(value as BackupInfo));
        DeleteBackupCommand = new RelayCommand(async value => await DeleteBackupAsync(value as BackupInfo));
        OpenBackupFolderCommand = new RelayCommand(_ => backupService.OpenBackupFolder());
        SaveTimeZoneCommand = new RelayCommand(async _ => await SaveTimeZoneAsync());
        ClearAllDataCommand = new RelayCommand(async _ => await ClearAllDataAsync());
        DeleteDataBefore30DaysCommand = new RelayCommand(async _ => await DeleteDataBeforeAsync(30));
        DeleteDataBefore90DaysCommand = new RelayCommand(async _ => await DeleteDataBeforeAsync(90));
        VacuumDatabaseCommand = new RelayCommand(async _ => await RunDataOperationAsync("正在压缩数据库...", databaseService.VacuumAsync));
        RebuildIndexesCommand = new RelayCommand(async _ => await RunDataOperationAsync("正在重建索引...", databaseService.RebuildIndexesAsync));
        HealthCheckCommand = new RelayCommand(async _ => await RunHealthCheckAsync());
        OpenDataDirectoryCommand = new RelayCommand(_ => OpenDirectory(AppPaths.DataDirectory));
        OpenLogDirectoryCommand = new RelayCommand(_ => OpenDirectory(AppPaths.LogsDirectory));
        OpenExportDirectoryCommand = new RelayCommand(_ => OpenDirectory(AppPaths.ExportsDirectory));
        StartQuickFocusCommand = new RelayCommand(async value => await StartQuickFocusAsync(value?.ToString() ?? "25"));
        StartFocusSessionCommand = new RelayCommand(async _ => await StartFocusSessionAsync());
        PauseFocusSessionCommand = new RelayCommand(async _ => { await focusSessionService.PauseAsync(); RefreshFocusSessionState(); });
        ResumeFocusSessionCommand = new RelayCommand(async _ => { await focusSessionService.ResumeAsync(); RefreshFocusSessionState(); });
        EndFocusSessionCommand = new RelayCommand(async _ => { await focusSessionService.EndAsync(); RefreshFocusSessionState(); await RefreshTodayFocusSummaryAsync(); await RefreshRecentFocusSessionsAsync(); });
        AbandonFocusSessionCommand = new RelayCommand(async _ => await AbandonFocusSessionAsync());

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await TickAsync();
        timer.Start();
        StatusText = "正在加载数据...";
        _ = LoadInitialDataAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RequestHideToTray;
    public event EventHandler<NotificationMessage>? RequestNotification;

    public ObservableCollection<AppUsageStat> Ranking { get; } = [];
    public ObservableCollection<TimelineItem> Timeline { get; } = [];
    public ObservableCollection<AppUsageMultiRangeStat> HistoryRanking { get; } = [];
    public ObservableCollection<DailyUsageStat> DailyTrend { get; } = [];
    public ObservableCollection<UsageShareItem> HistoryAppShare { get; } = [];
    public ObservableCollection<UsageShareItem> HistoryCategoryShare { get; } = [];
    public ObservableCollection<DailyUsageStat> ReportDailyTrend { get; } = [];
    public ObservableCollection<UsageShareItem> ReportTopApps { get; } = [];
    public ObservableCollection<UsageShareItem> ReportTopCategories { get; } = [];
    public ObservableCollection<ManagedAppItem> ManagedApps { get; } = [];
    public ObservableCollection<FocusRuleStatus> FocusRules { get; } = [];
    public ObservableCollection<FocusSession> RecentFocusSessions { get; } = [];
    public ObservableCollection<BackupInfo> Backups { get; } = [];
    public ObservableCollection<string> CategoryOptions { get; }
    public ObservableCollection<SelectOption> TimeZoneOptions { get; } =
    [
        new("system", "跟随系统时区"),
        new("China Standard Time", "中国标准时间"),
        new("GMT Standard Time", "英国时间"),
        new("Tokyo Standard Time", "日本时间"),
        new("Eastern Standard Time", "美国东部时间")
    ];
    public ObservableCollection<SelectOption> FocusTargetTypeOptions { get; } =
    [
        new("app", "应用"),
        new("category", "分类")
    ];
    public ObservableCollection<SelectOption> FocusRuleTypeOptions { get; } =
    [
        new("max", "最多使用"),
        new("min", "至少使用")
    ];
    public ObservableCollection<SelectOption> ReportRangeOptions { get; } =
    [
        new("ThisWeek", "本周"),
        new("LastWeek", "上周"),
        new("ThisMonth", "本月"),
        new("LastMonth", "上月"),
        new("Last7Days", "最近7天"),
        new("Last30Days", "最近30天")
    ];

    public ICommand ClearTodayCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand SetHistoryRangeCommand { get; }
    public ICommand PauseTrackingCommand { get; }
    public ICommand ResumeTrackingCommand { get; }
    public ICommand HideToTrayCommand { get; }
    public ICommand SaveFocusRuleCommand { get; }
    public ICommand ResetFocusRuleFormCommand { get; }
    public ICommand EditFocusRuleCommand { get; }
    public ICommand DeleteFocusRuleCommand { get; }
    public ICommand SetReportRangeCommand { get; }
    public ICommand RefreshDatabaseInfoCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportReportTextCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand RestoreDatabaseCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand DeleteBackupCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }
    public ICommand SaveTimeZoneCommand { get; }
    public ICommand ClearAllDataCommand { get; }
    public ICommand DeleteDataBefore30DaysCommand { get; }
    public ICommand DeleteDataBefore90DaysCommand { get; }
    public ICommand VacuumDatabaseCommand { get; }
    public ICommand RebuildIndexesCommand { get; }
    public ICommand HealthCheckCommand { get; }
    public ICommand OpenDataDirectoryCommand { get; }
    public ICommand OpenLogDirectoryCommand { get; }
    public ICommand OpenExportDirectoryCommand { get; }
    public ICommand StartQuickFocusCommand { get; }
    public ICommand StartFocusSessionCommand { get; }
    public ICommand PauseFocusSessionCommand { get; }
    public ICommand ResumeFocusSessionCommand { get; }
    public ICommand EndFocusSessionCommand { get; }
    public ICommand AbandonFocusSessionCommand { get; }

    public int IdleThresholdSeconds
    {
        get => idleThresholdSeconds;
        set { idleThresholdSeconds = Math.Max(1, value); OnPropertyChanged(); }
    }

    public bool RecordWindowTitle
    {
        get => recordWindowTitle;
        set { recordWindowTitle = value; OnPropertyChanged(); }
    }

    public bool AutoStart
    {
        get => autoStart;
        set
        {
            if (!SetField(ref autoStart, value)) return;
            databaseService.SetBoolSetting("AutoStart", value);
            ApplyAutoStartSetting(value);
        }
    }

    public bool StartMinimizedToTray
    {
        get => startMinimizedToTray;
        set
        {
            if (!SetField(ref startMinimizedToTray, value)) return;
            databaseService.SetBoolSetting("StartMinimizedToTray", value);

            if (AutoStart)
            {
                ApplyAutoStartSetting(true);
            }
        }
    }

    public bool CloseToTray
    {
        get => closeToTray;
        set
        {
            if (!SetField(ref closeToTray, value)) return;
            databaseService.SetBoolSetting("CloseToTray", value);
        }
    }

    public bool ManualPaused
    {
        get => manualPaused;
        set
        {
            if (!SetField(ref manualPaused, value)) return;

            if (value)
            {
                trackingService.Pause();
            }
            else
            {
                trackingService.Resume();
            }

            databaseService.SetBoolSetting("ManualPaused", value);
            RefreshTodayDashboard();
        }
    }

    public bool AutoDailyBackupEnabled
    {
        get => autoDailyBackupEnabled;
        set
        {
            if (!SetField(ref autoDailyBackupEnabled, value)) return;
            databaseService.SetBoolSetting("Backup.AutoDailyEnabled", value);
        }
    }

    public bool BackupOnExitEnabled
    {
        get => backupOnExitEnabled;
        set
        {
            if (!SetField(ref backupOnExitEnabled, value)) return;
            databaseService.SetBoolSetting("Backup.OnExitEnabled", value);
        }
    }

    public int MaxAutoBackups
    {
        get => maxAutoBackups;
        set
        {
            var safeValue = Math.Clamp(value, 1, 365);
            if (!SetField(ref maxAutoBackups, safeValue)) return;
            databaseService.SetSetting("Backup.MaxAutoBackups", safeValue.ToString());
        }
    }

    public string TimeZoneMode
    {
        get => timeZoneMode;
        set => SetField(ref timeZoneMode, value);
    }

    public string SelectedTimeZoneId
    {
        get => selectedTimeZoneId;
        set
        {
            if (!SetField(ref selectedTimeZoneId, value)) return;
            TimeZoneMode = value == "system" ? "system" : "fixed";
        }
    }

    public string TodayTotalTime { get => todayTotalTime; private set => SetField(ref todayTotalTime, value); }
    public string TodayTopApp { get => todayTopApp; private set => SetField(ref todayTopApp, value); }
    public string CurrentApp { get => currentApp; private set => SetField(ref currentApp, value); }
    public string CurrentDuration { get => currentDuration; private set => SetField(ref currentDuration, value); }
    public string StatusText { get => statusText; private set => SetField(ref statusText, value); }
    public string HistoryRangeTitle { get => historyRangeTitle; private set => SetField(ref historyRangeTitle, value); }
    public string HistoryTotalTime { get => historyTotalTime; private set => SetField(ref historyTotalTime, value); }
    public string HistoryTopApp { get => historyTopApp; private set => SetField(ref historyTopApp, value); }
    public string HistoryAverageDaily { get => historyAverageDaily; private set => SetField(ref historyAverageDaily, value); }
    public string HistoryAppCount { get => historyAppCount; private set => SetField(ref historyAppCount, value); }
    public string HistoryTrendTitle { get => historyTrendTitle; private set => SetField(ref historyTrendTitle, value); }
    public string ReportRangeTitle { get => reportRangeTitle; private set => SetField(ref reportRangeTitle, value); }
    public string ReportTotalTime { get => reportTotalTime; private set => SetField(ref reportTotalTime, value); }
    public string ReportAverageDaily { get => reportAverageDaily; private set => SetField(ref reportAverageDaily, value); }
    public string ReportTopApp { get => reportTopApp; private set => SetField(ref reportTopApp, value); }
    public string ReportTopCategory { get => reportTopCategory; private set => SetField(ref reportTopCategory, value); }
    public string ReportAppCount { get => reportAppCount; private set => SetField(ref reportAppCount, value); }
    public string ReportRuleNotificationCount { get => reportRuleNotificationCount; private set => SetField(ref reportRuleNotificationCount, value); }
    public string ReportSummaryText { get => reportSummaryText; private set => SetField(ref reportSummaryText, value); }
    public string DataStatusText { get => dataStatusText; private set => SetField(ref dataStatusText, value); }
    public string DataHealthCheckResult { get => dataHealthCheckResult; private set => SetField(ref dataHealthCheckResult, value); }
    public string DatabaseFileSize { get => databaseFileSize; private set => SetField(ref databaseFileSize, value); }
    public string DatabaseSessionCount { get => databaseSessionCount; private set => SetField(ref databaseSessionCount, value); }
    public string DatabaseAppCount { get => databaseAppCount; private set => SetField(ref databaseAppCount, value); }
    public string DatabaseEarliestRecord { get => databaseEarliestRecord; private set => SetField(ref databaseEarliestRecord, value); }
    public string DatabaseLatestRecord { get => databaseLatestRecord; private set => SetField(ref databaseLatestRecord, value); }
    public bool IsDataOperationRunning { get => isDataOperationRunning; private set => SetField(ref isDataOperationRunning, value); }
    public string FocusSessionStatus { get => focusSessionStatus; private set => SetField(ref focusSessionStatus, value); }
    public string FocusSessionRemaining { get => focusSessionRemaining; private set => SetField(ref focusSessionRemaining, value); }
    public string FocusSessionActual { get => focusSessionActual; private set => SetField(ref focusSessionActual, value); }
    public string FocusSessionDistractions { get => focusSessionDistractions; private set => SetField(ref focusSessionDistractions, value); }
    public string FocusSessionCurrentName { get => focusSessionCurrentName; private set => SetField(ref focusSessionCurrentName, value); }
    public string TodayFocusCompletedCount { get => todayFocusCompletedCount; private set => SetField(ref todayFocusCompletedCount, value); }
    public string TodayFocusTotalTime { get => todayFocusTotalTime; private set => SetField(ref todayFocusTotalTime, value); }
    public string TodayFocusDistractionCount { get => todayFocusDistractionCount; private set => SetField(ref todayFocusDistractionCount, value); }
    public string ReportFocusCompletedCount { get => reportFocusCompletedCount; private set => SetField(ref reportFocusCompletedCount, value); }
    public string ReportFocusTotalTime { get => reportFocusTotalTime; private set => SetField(ref reportFocusTotalTime, value); }
    public string ReportFocusAverageTime { get => reportFocusAverageTime; private set => SetField(ref reportFocusAverageTime, value); }
    public string ReportFocusDistractionCount { get => reportFocusDistractionCount; private set => SetField(ref reportFocusDistractionCount, value); }
    public string FocusNotificationCount { get => focusNotificationCount; private set => SetField(ref focusNotificationCount, value); }
    public string FocusOverLimitCount { get => focusOverLimitCount; private set => SetField(ref focusOverLimitCount, value); }
    public string FocusCompletedCount { get => focusCompletedCount; private set => SetField(ref focusCompletedCount, value); }
    public string FocusTodayState { get => focusTodayState; private set => SetField(ref focusTodayState, value); }
    public string FocusFormTitle { get => focusFormTitle; private set => SetField(ref focusFormTitle, value); }
    public string DatabasePath => databaseService.DatabasePath;
    public string AppVersion => AppPaths.Version;
    public string DataDirectory => AppPaths.DataDirectory;
    public string LogDirectory => AppPaths.LogsDirectory;
    public string ExportDirectory => AppPaths.ExportsDirectory;
    public string BackupDirectory => AppPaths.BackupsDirectory;
    public string SystemTimeZoneDisplayName => TimeZoneInfo.Local.DisplayName + " (" + TimeZoneInfo.Local.Id + ")";
    public string SelectedTimeZoneDisplayName => timeZoneService.GetCurrentTimeZoneDisplayName();
    public string GitHubProjectUrl => "https://github.com/your-name/time-controller";
    public string AuthorInfo => "作者信息占位";

    public string FocusRuleName
    {
        get => focusRuleName;
        set => SetField(ref focusRuleName, value);
    }

    public string FocusTargetType
    {
        get => focusTargetType;
        set => SetField(ref focusTargetType, value);
    }

    public string FocusAppProcessName
    {
        get => focusAppProcessName;
        set => SetField(ref focusAppProcessName, value);
    }

    public string FocusCategory
    {
        get => focusCategory;
        set => SetField(ref focusCategory, value);
    }

    public string FocusRuleType
    {
        get => focusRuleType;
        set => SetField(ref focusRuleType, value);
    }

    public int FocusLimitMinutes
    {
        get => focusLimitMinutes;
        set => SetField(ref focusLimitMinutes, Math.Max(1, value));
    }

    public bool FocusRuleEnabled
    {
        get => focusRuleEnabled;
        set => SetField(ref focusRuleEnabled, value);
    }

    public bool FocusNotifyEnabled
    {
        get => focusNotifyEnabled;
        set => SetField(ref focusNotifyEnabled, value);
    }

    public string FocusSessionName { get => focusSessionName; set => SetField(ref focusSessionName, value); }
    public string FocusSessionTargetType { get => focusSessionTargetType; set => SetField(ref focusSessionTargetType, value); }
    public string FocusSessionAppProcessName { get => focusSessionAppProcessName; set => SetField(ref focusSessionAppProcessName, value); }
    public string FocusSessionCategory { get => focusSessionCategory; set => SetField(ref focusSessionCategory, value); }
    public int FocusSessionMinutes { get => focusSessionMinutes; set => SetField(ref focusSessionMinutes, Math.Max(1, value)); }
    public bool FocusSessionAllowBrowser { get => focusSessionAllowBrowser; set => SetField(ref focusSessionAllowBrowser, value); }
    public bool FocusSessionAllowSystemTools { get => focusSessionAllowSystemTools; set => SetField(ref focusSessionAllowSystemTools, value); }
    public bool FocusSessionNotifyEnabled { get => focusSessionNotifyEnabled; set => SetField(ref focusSessionNotifyEnabled, value); }

    public void PauseTracking()
    {
        ManualPaused = true;
    }

    public void ResumeTracking()
    {
        ManualPaused = false;
    }

    public void StopTracking()
    {
        timer.Stop();
        focusSessionService.AbandonAsync().GetAwaiter().GetResult();
        trackingService.Stop();
        RefreshTodayDashboard();
    }

    public void Dispose() => StopTracking();

    public async Task LoadHistoryPageAsync()
    {
        StatusText = "正在加载历史统计...";
        await Task.Yield();
        RefreshHistoryDashboard();
        StatusText = GetStatusText();
    }

    public async Task LoadAppManagementPageAsync()
    {
        StatusText = "正在加载应用管理...";
        await Task.Yield();
        RefreshManagedApps();
        StatusText = GetStatusText();
    }

    public async Task LoadFocusRulesPageAsync()
    {
        StatusText = "正在加载专注规则...";
        await Task.Yield();
        RefreshFocusRules();
        StatusText = GetStatusText();
    }

    public async Task LoadFocusSessionPageAsync()
    {
        StatusText = "正在加载专注会话...";
        await Task.Yield();
        RefreshManagedApps();
        RefreshFocusSessionState();
        await RefreshRecentFocusSessionsAsync();
        StatusText = GetStatusText();
    }

    public async Task LoadReportsPageAsync()
    {
        StatusText = "正在加载报告数据...";
        await Task.Yield();
        RefreshReportDashboard();
        StatusText = GetStatusText();
    }

    public async Task LoadDataSettingsPageAsync()
    {
        await RefreshDatabaseInfoAsync();
        await RefreshBackupsAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        await Task.Yield();
        await EnsureDailyAutoBackupAsync();
        RefreshTodayDashboard();
        await RefreshTodayFocusSummaryAsync();
        await RefreshDatabaseInfoAsync();
        await RefreshBackupsAsync();
        StatusText = GetStatusText();
    }

    private void LoadSettings()
    {
        autoStart = databaseService.GetBoolSetting("AutoStart", false);
        startMinimizedToTray = databaseService.GetBoolSetting("StartMinimizedToTray", false);
        closeToTray = databaseService.GetBoolSetting("CloseToTray", true);
        manualPaused = databaseService.GetBoolSetting("ManualPaused", false);
        autoDailyBackupEnabled = databaseService.GetBoolSetting("Backup.AutoDailyEnabled", true);
        backupOnExitEnabled = databaseService.GetBoolSetting("Backup.OnExitEnabled", true);
        maxAutoBackups = int.TryParse(databaseService.GetSetting("Backup.MaxAutoBackups", "30"), out var parsedBackups)
            ? Math.Clamp(parsedBackups, 1, 365)
            : 30;
        timeZoneMode = databaseService.GetSetting("TimeZone.Mode", "system");
        selectedTimeZoneId = timeZoneMode == "system"
            ? "system"
            : databaseService.GetSetting("TimeZone.Id", TimeZoneInfo.Local.Id);
        timeZoneService.LoadSettingsAsync().GetAwaiter().GetResult();

        if (manualPaused)
        {
            trackingService.Pause();
        }
    }

    private void ApplyAutoStartSetting(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);

            if (key is null)
            {
                StatusText = "Unable to open auto-start registry key";
                LogService.Warning("Auto start registry key could not be opened");
                return;
            }

            if (enabled)
            {
                var executablePath = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? "";

                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    StatusText = "Unable to determine executable path";
                    LogService.Warning("Auto start executable path was empty");
                    return;
                }

                var arguments = StartMinimizedToTray ? " --minimized-to-tray" : "";
                key.SetValue("time-controller", $"\"{executablePath}\"{arguments}");
                LogService.Info("Auto start enabled");
            }
            else
            {
                key.DeleteValue("time-controller", throwOnMissingValue: false);
                LogService.Info("Auto start disabled");
            }
        }
        catch (Exception ex)
        {
            LogService.Exception("Auto start setting failed", ex);
            StatusText = "Auto-start setting failed: " + ex.Message;
        }
    }
    private async Task TickAsync()
    {
        try
        {
            trackingService.Tick(TimeSpan.FromSeconds(IdleThresholdSeconds), RecordWindowTitle);
            await focusSessionService.TickAsync(trackingService.CurrentProcessName, trackingService.CurrentAppName);
            CurrentApp = GetCurrentAppText();
            CurrentDuration = TimeFormatter.Format(trackingService.CurrentDurationSeconds);
            RefreshFocusSessionState();
            StatusText = GetStatusText();

            if (DateTime.Now >= nextTodayRefresh)
            {
                nextTodayRefresh = DateTime.Now.AddSeconds(5);
                RefreshTodayDashboard();
            }

            if (DateTime.Now >= nextFocusRuleCheck)
            {
                nextFocusRuleCheck = DateTime.Now.AddSeconds(30);
                RefreshFocusRules();
            }
        }
        catch (Exception ex)
        {
            LogService.Exception("Tracking tick failed", ex);
            StatusText = "Tracking error: " + ex.Message;
        }
    }
    private void RefreshDashboard()
    {
        RefreshTodayDashboard();
    }

    private void RefreshTodayDashboard()
    {
        var ranking = databaseService.GetTodayUsageStats();
        var totalUsageByApp = databaseService.GetTotalUsageSecondsByApp();
        var timeline = databaseService.GetTodayTimeline();

        foreach (var item in ranking)
        {
            item.TotalDurationSeconds = totalUsageByApp.GetValueOrDefault(item.AppId);
        }

        AddLiveSessionToToday(ranking, timeline);
        var orderedRanking = ranking.OrderByDescending(item => item.TodayDurationSeconds).ToList();
        AssignRanks(orderedRanking);

        TodayTotalTime = TimeFormatter.Format(orderedRanking.Sum(item => item.TodayDurationSeconds));
        TodayTopApp = orderedRanking.FirstOrDefault()?.DisplayName ?? "暂无";
        CurrentApp = GetCurrentAppText();
        CurrentDuration = TimeFormatter.Format(trackingService.CurrentDurationSeconds);
        StatusText = GetStatusText();

        ApplyTimelineDisplayTimes(timeline);
        Replace(Ranking, orderedRanking);
        Replace(Timeline, timeline.Where(item => item.DurationSeconds >= 3).OrderByDescending(item => item.StartTime));
        RefreshTodayFocusSummaryAsync().GetAwaiter().GetResult();
    }

    private void RefreshHistoryDashboard()
    {
        var range = ResolveHistoryRange(currentHistoryRange);
        var rangeStats = databaseService.GetUsageStats(range.Start, range.End);
        AddLiveSessionToHistory(rangeStats, range.Start, range.End);

        var multiRangeStats = databaseService.GetAppUsageMultiRangeStatsAsync().GetAwaiter().GetResult();
        AddLiveSessionToMultiRange(multiRangeStats);
        var orderedMultiRangeStats = multiRangeStats
            .OrderByDescending(item => item.Last7DaysDurationSeconds)
            .ThenByDescending(item => item.TotalDurationSeconds)
            .ToList();
        AssignRanks(orderedMultiRangeStats);

        var totalSeconds = rangeStats.Sum(item => item.RangeDurationSeconds);
        HistoryRangeTitle = range.Title;
        HistoryTotalTime = TimeFormatter.Format(totalSeconds);
        HistoryTopApp = rangeStats.OrderByDescending(item => item.RangeDurationSeconds).FirstOrDefault()?.DisplayName ?? "暂无";
        HistoryAverageDaily = TimeFormatter.Format(GetAverageDailySeconds(totalSeconds, range));
        HistoryAppCount = rangeStats.Count.ToString();
        HistoryTrendTitle = currentHistoryRange == HistoryRange.Last30Days ? "最近30天每日趋势" : "最近7天每日趋势";

        Replace(HistoryRanking, orderedMultiRangeStats);
        Replace(DailyTrend, BuildHistoryTrend(range));
        Replace(HistoryAppShare, databaseService.GetAppUsageShareAsync(GetRangeStart(range), GetRangeEnd(range)).GetAwaiter().GetResult().Take(8));
        Replace(HistoryCategoryShare, databaseService.GetCategoryUsageShareAsync(GetRangeStart(range), GetRangeEnd(range)).GetAwaiter().GetResult().Take(8));
    }

    private void RefreshReportDashboard()
    {
        try
        {
            var range = ResolveReportRange(currentReportRange);
            var summary = databaseService.GetReportSummaryAsync(range.Start, range.End).GetAwaiter().GetResult();
            var topApps = databaseService.GetTopAppsAsync(range.Start, range.End, 5).GetAwaiter().GetResult();
            var topCategories = databaseService.GetTopCategoriesAsync(range.Start, range.End, 6).GetAwaiter().GetResult();
            var trend = databaseService.GetDailyUsageTrendAsync(range.Start, range.End).GetAwaiter().GetResult();
            AddLiveSessionToTrend(trend, range.Start, range.End);
            ApplyReportTrendHeights(trend);
            var completedFocus = databaseService.GetCompletedFocusCountAsync(range.Start, range.End).GetAwaiter().GetResult();
            var totalFocusSeconds = databaseService.GetTotalFocusSecondsAsync(range.Start, range.End).GetAwaiter().GetResult();
            var totalFocusDistractions = databaseService.GetTotalDistractionCountAsync(range.Start, range.End).GetAwaiter().GetResult();
            var averageFocusSeconds = completedFocus == 0 ? 0 : totalFocusSeconds / completedFocus;

            ReportRangeTitle = range.Title;
            ReportTotalTime = summary.TotalDurationText;
            ReportAverageDaily = summary.AverageDailyDurationText;
            ReportTopApp = summary.MostUsedApp;
            ReportTopCategory = summary.MostUsedCategory;
            ReportAppCount = summary.AppCount.ToString();
            ReportRuleNotificationCount = summary.RuleNotificationCount.ToString();
            ReportFocusCompletedCount = completedFocus.ToString();
            ReportFocusTotalTime = TimeFormatter.Format(totalFocusSeconds);
            ReportFocusAverageTime = TimeFormatter.Format(averageFocusSeconds);
            ReportFocusDistractionCount = totalFocusDistractions.ToString();
            ReportSummaryText = BuildReportSummaryText(
                summary,
                topApps,
                topCategories,
                trend,
                completedFocus,
                totalFocusSeconds,
                averageFocusSeconds,
                totalFocusDistractions);

            Replace(ReportTopApps, topApps);
            Replace(ReportTopCategories, topCategories);
            Replace(ReportDailyTrend, trend);
        }
        catch (Exception ex)
        {
            StatusText = "报告刷新失败：" + ex.Message;
        }
    }

    private void RefreshManagedApps()
    {
        var apps = databaseService.GetAllAppsWithStatsAsync().GetAwaiter().GetResult();
        isLoadingManagedApps = true;
        try
        {
            foreach (var app in apps)
            {
                var existing = ManagedApps.FirstOrDefault(item => item.ProcessName == app.ProcessName);
                if (existing is null)
                {
                    ManagedApps.Add(new ManagedAppItem(app));
                    continue;
                }

                existing.UpdateStats(app.TodayDurationSeconds, app.Last7DaysDurationSeconds, app.Last30DaysDurationSeconds, app.TotalDurationSeconds);
            }

            if (string.IsNullOrWhiteSpace(FocusAppProcessName) && ManagedApps.Count > 0)
            {
                FocusAppProcessName = ManagedApps[0].ProcessName;
            }

            if (string.IsNullOrWhiteSpace(FocusSessionAppProcessName) && ManagedApps.Count > 0)
            {
                FocusSessionAppProcessName = ManagedApps[0].ProcessName;
            }
        }
        finally
        {
            isLoadingManagedApps = false;
        }
    }

    private void RefreshFocusRules()
    {
        try
        {
            var liveProcessName = trackingService.CurrentProcessName;
            var liveDurationSeconds = trackingService.CurrentDurationSeconds;
            var statuses = focusRuleService.GetRuleStatusesAsync(liveProcessName, liveDurationSeconds).GetAwaiter().GetResult();

            isLoadingFocusRules = true;
            try
            {
                foreach (var item in FocusRules)
                {
                    item.PropertyChanged -= FocusRuleStatus_PropertyChanged;
                }

                Replace(FocusRules, statuses);
            }
            finally
            {
                isLoadingFocusRules = false;
            }

            foreach (var item in FocusRules)
            {
                item.PropertyChanged += FocusRuleStatus_PropertyChanged;
            }

            var enabledRules = statuses.Where(item => item.IsEnabled).ToList();
            FocusNotificationCount = databaseService.GetTodayFocusNotificationCountAsync().GetAwaiter().GetResult().ToString();
            FocusOverLimitCount = enabledRules.Count(item => item.IsOverLimit).ToString();
            FocusCompletedCount = enabledRules.Count(item => item.IsCompleted).ToString();
            FocusTodayState = enabledRules.Any(item => item.IsOverLimit)
                ? "已超时"
                : enabledRules.Any(item => item.RuleType == "min" && !item.IsCompleted)
                    ? "目标未完成"
                    : "正常";
        }
        catch (Exception ex)
        {
            StatusText = "专注规则刷新失败：" + ex.Message;
        }
    }

    private async Task StartQuickFocusAsync(string minutesText)
    {
        var minutes = int.TryParse(minutesText, out var parsed) ? parsed : 25;
        FocusSessionMinutes = minutes;
        FocusSessionName = minutes switch
        {
            45 => "45 分钟深度专注",
            60 => "60 分钟学习",
            _ => "25 分钟专注"
        };
        await StartFocusSessionAsync();
    }

    private async Task StartFocusSessionAsync()
    {
        var targetValue = FocusSessionTargetType == "category" ? FocusSessionCategory : FocusSessionAppProcessName;
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            StatusText = "请先选择专注目标";
            return;
        }

        await focusSessionService.StartAsync(
            FocusSessionName,
            FocusSessionTargetType,
            targetValue,
            FocusSessionMinutes * 60,
            FocusSessionAllowBrowser,
            FocusSessionAllowSystemTools,
            FocusSessionNotifyEnabled);
        RefreshFocusSessionState();
        await RefreshTodayFocusSummaryAsync();
    }

    private async Task AbandonFocusSessionAsync()
    {
        await focusSessionService.AbandonAsync();
        RefreshFocusSessionState();
        await RefreshTodayFocusSummaryAsync();
        await RefreshRecentFocusSessionsAsync();
    }

    private void RefreshFocusSessionState()
    {
        FocusSessionStatus = focusSessionService.StatusText;
        FocusSessionRemaining = TimeFormatter.Format(focusSessionService.RemainingSeconds);
        FocusSessionActual = TimeFormatter.Format(focusSessionService.ActualSeconds);
        FocusSessionDistractions = focusSessionService.DistractionCount.ToString();
        FocusSessionCurrentName = focusSessionService.CurrentSession?.Name ?? "暂无";
    }

    private async Task RefreshTodayFocusSummaryAsync()
    {
        var summary = await databaseService.GetTodayFocusSummaryAsync();
        TodayFocusCompletedCount = summary.CompletedCount.ToString();
        TodayFocusTotalTime = summary.TotalFocusText;
        TodayFocusDistractionCount = summary.TotalDistractionCount.ToString();
    }

    private async Task RefreshRecentFocusSessionsAsync()
    {
        Replace(RecentFocusSessions, await databaseService.GetFocusSessionsAsync(DateTime.Today.AddDays(-7), DateTime.Today.AddDays(1)));
    }

    private void NotificationService_NotificationRaised(object? sender, NotificationMessage e)
    {
        StatusText = e.Title + "：" + e.Message;
        RequestNotification?.Invoke(this, e);
    }

    private async void FocusRuleStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isLoadingFocusRules || sender is not FocusRuleStatus status || e.PropertyName != nameof(FocusRuleStatus.IsEnabled))
        {
            return;
        }

        try
        {
            await databaseService.SetFocusRuleEnabledAsync(status.Id, status.IsEnabled);
            RefreshFocusRules();
        }
        catch (Exception ex)
        {
            StatusText = "规则启用状态保存失败：" + ex.Message;
        }
    }

    private async void ManagedApp_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isLoadingManagedApps || sender is not ManagedAppItem app)
        {
            return;
        }

        try
        {
            switch (e.PropertyName)
            {
                case nameof(ManagedAppItem.DisplayName):
                    await databaseService.UpdateAppDisplayNameAsync(app.ProcessName, app.DisplayName);
                    break;
                case nameof(ManagedAppItem.Category):
                    await databaseService.UpdateAppCategoryAsync(app.ProcessName, app.Category);
                    break;
                case nameof(ManagedAppItem.IsIgnored):
                    await databaseService.UpdateAppIgnoredAsync(app.ProcessName, app.IsIgnored);
                    trackingService.Stop();
                    break;
            }

            RefreshTodayDashboard();
            RefreshHistoryDashboard();
        }
        catch (Exception ex)
        {
            StatusText = "保存应用设置失败：" + ex.Message;
        }
    }

    private void ManagedApps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ManagedAppItem item in e.NewItems)
            {
                item.PropertyChanged += ManagedApp_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ManagedAppItem item in e.OldItems)
            {
                item.PropertyChanged -= ManagedApp_PropertyChanged;
            }
        }
    }

    private async void SaveFocusRule()
    {
        try
        {
            var targetValue = FocusTargetType == "category" ? FocusCategory : FocusAppProcessName;
            if (string.IsNullOrWhiteSpace(targetValue))
            {
                StatusText = "请先选择规则对象";
                return;
            }

            var rule = new FocusRule
            {
                Id = editingFocusRuleId,
                Name = string.IsNullOrWhiteSpace(FocusRuleName) ? BuildDefaultFocusRuleName() : FocusRuleName.Trim(),
                TargetType = FocusTargetType,
                TargetValue = targetValue,
                RuleType = FocusRuleType,
                LimitSeconds = FocusLimitMinutes * 60,
                IsEnabled = FocusRuleEnabled,
                NotifyEnabled = FocusNotifyEnabled,
                CreatedAt = DateTime.Now
            };

            if (editingFocusRuleId > 0)
            {
                await databaseService.UpdateFocusRuleAsync(rule);
                StatusText = "专注规则已更新";
            }
            else
            {
                await databaseService.AddFocusRuleAsync(rule);
                StatusText = "专注规则已新增";
            }

            ResetFocusRuleForm();
            RefreshFocusRules();
        }
        catch (Exception ex)
        {
            StatusText = "保存专注规则失败：" + ex.Message;
        }
    }

    private void EditFocusRule(FocusRuleStatus? status)
    {
        if (status is null)
        {
            return;
        }

        editingFocusRuleId = status.Id;
        FocusFormTitle = "编辑规则";
        FocusRuleName = status.Name;
        FocusTargetType = status.TargetType;
        FocusRuleType = status.RuleType;
        FocusLimitMinutes = Math.Max(1, status.LimitSeconds / 60);
        FocusRuleEnabled = status.IsEnabled;
        FocusNotifyEnabled = status.NotifyEnabled;

        if (status.TargetType == "category")
        {
            FocusCategory = status.TargetValue;
        }
        else
        {
            FocusAppProcessName = status.TargetValue;
        }
    }

    private async void DeleteFocusRule(FocusRuleStatus? status)
    {
        if (status is null)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"确定删除规则“{status.Name}”吗？",
            "删除专注规则",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await databaseService.DeleteFocusRuleAsync(status.Id);
            StatusText = "专注规则已删除";
            RefreshFocusRules();
        }
        catch (Exception ex)
        {
            StatusText = "删除专注规则失败：" + ex.Message;
        }
    }

    private void ResetFocusRuleForm()
    {
        editingFocusRuleId = 0;
        FocusFormTitle = "新增规则";
        FocusRuleName = "";
        FocusTargetType = "app";
        FocusRuleType = "max";
        FocusLimitMinutes = 60;
        FocusRuleEnabled = true;
        FocusNotifyEnabled = true;

        if (ManagedApps.Count > 0)
        {
            FocusAppProcessName = ManagedApps[0].ProcessName;
        }

        FocusCategory = CategoryOptions.FirstOrDefault() ?? "其他";
    }

    private string BuildDefaultFocusRuleName()
    {
        var target = FocusTargetType == "category"
            ? FocusCategory
            : ManagedApps.FirstOrDefault(item => item.ProcessName == FocusAppProcessName)?.DisplayName ?? FocusAppProcessName;
        var ruleType = FocusRuleType == "min" ? "至少使用" : "最多使用";
        return $"{target} {ruleType} {FocusLimitMinutes} 分钟";
    }

    private void FocusRuleService_RuleNotificationRaised(object? sender, FocusRuleNotificationEventArgs e)
    {
        notificationService.Warning("time-controller 提醒", e.Message);
    }

    private string GetCurrentAppText()
    {
        if (trackingService.IsManuallyPaused) return "已暂停";
        if (trackingService.IsPausedByIdle) return "空闲暂停";
        if (trackingService.IsPausedByIgnoredApp) return "已忽略应用";
        return trackingService.CurrentAppName;
    }

    private string GetStatusText()
    {
        if (trackingService.IsManuallyPaused) return "已暂停";
        if (trackingService.IsPausedByIdle) return "空闲暂停";
        if (trackingService.IsPausedByIgnoredApp) return "暂停统计";
        return "正在统计";
    }

    private void ClearTodayData()
    {
        var result = System.Windows.MessageBox.Show("确定清空今日数据吗？此操作不可恢复。", "清空今日数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        trackingService.Stop();
        databaseService.ClearTodayData();
        trackingService.ResetAfterDataClear();
        RefreshTodayDashboard();
    }

    private async Task RefreshDatabaseInfoAsync()
    {
        try
        {
            DataStatusText = "正在加载数据库信息...";
            var info = await databaseService.GetDatabaseInfoAsync();
            DatabaseFileSize = info.FileSizeText;
            DatabaseSessionCount = info.UsageSessionCount.ToString();
            DatabaseAppCount = info.AppCount.ToString();
            DatabaseEarliestRecord = info.EarliestRecordText;
            DatabaseLatestRecord = info.LatestRecordText;
            DataStatusText = "数据库信息已刷新";
        }
        catch (Exception ex)
        {
            DataStatusText = "数据库信息加载失败：" + ex.Message;
        }
    }

    private async Task RefreshBackupsAsync()
    {
        try
        {
            Replace(Backups, await backupService.GetBackupsAsync());
        }
        catch (Exception ex)
        {
            DataStatusText = "备份列表加载失败：" + ex.Message;
            LogService.Exception("Backup list refresh failed", ex);
        }
    }

    private async Task EnsureDailyAutoBackupAsync()
    {
        if (!AutoDailyBackupEnabled)
        {
            return;
        }

        var today = timeZoneService.GetSelectedZoneDateString(DateTime.UtcNow);
        var lastDate = databaseService.GetSetting("Backup.LastAutoBackupDate", "");
        if (lastDate == today)
        {
            return;
        }

        try
        {
            await backupService.CreateBackupAsync("auto");
            await backupService.CleanupOldBackupsAsync(MaxAutoBackups);
            databaseService.SetSetting("Backup.LastAutoBackupDate", today);
            await RefreshBackupsAsync();
        }
        catch (Exception ex)
        {
            LogService.Exception("Daily auto backup failed", ex);
        }
    }

    public async Task CreateExitBackupIfEnabledAsync()
    {
        if (!BackupOnExitEnabled)
        {
            return;
        }

        try
        {
            await backupService.CreateBackupAsync("exit");
            await backupService.CleanupOldBackupsAsync(MaxAutoBackups);
        }
        catch (Exception ex)
        {
            LogService.Exception("Exit backup failed", ex);
        }
    }

    private async Task CreateManualBackupAsync()
    {
        await RunDataOperationAsync("正在创建备份...", async () =>
        {
            await backupService.CreateBackupAsync("manual");
            await RefreshBackupsAsync();
        });
        DataStatusText = "备份创建成功";
    }

    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确定恢复备份“{backup.FileName}”吗？当前数据库会先自动创建恢复前备份。",
            "恢复备份",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        trackingService.Stop();
        await RunDataOperationAsync("正在恢复备份...", async () =>
        {
            await backupService.RestoreBackupAsync(backup);
            trackingService.ResetAfterDataClear();
            RefreshTodayDashboard();
            await RefreshBackupsAsync();
        });
    }

    private async Task DeleteBackupAsync(BackupInfo? backup)
    {
        if (backup is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确定删除备份“{backup.FileName}”吗？",
            "删除备份",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunDataOperationAsync("正在删除备份...", async () =>
        {
            await backupService.DeleteBackupAsync(backup);
            await RefreshBackupsAsync();
        });
    }

    private async Task SaveTimeZoneAsync()
    {
        var mode = SelectedTimeZoneId == "system" ? "system" : "fixed";
        var zoneId = SelectedTimeZoneId == "system" ? TimeZoneInfo.Local.Id : SelectedTimeZoneId;
        await timeZoneService.SaveTimeZoneAsync(mode, zoneId);
        TimeZoneMode = mode;
        OnPropertyChanged(nameof(SelectedTimeZoneDisplayName));
        RefreshTodayDashboard();
        RefreshHistoryDashboard();
        RefreshReportDashboard();
        DataStatusText = "时区设置已保存";
    }

    private async Task ExportJsonAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 JSON",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"app-usage-export-{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dialog.ShowDialog() != true) return;
        await RunDataOperationAsync("正在导出 JSON...", () => databaseService.ExportJsonAsync(dialog.FileName));
    }

    private async Task ExportReportTextAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出报告摘要",
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"app-usage-report-{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true) return;
        await RunDataOperationAsync("正在导出报告摘要...", () => databaseService.ExportReportTextAsync(dialog.FileName));
    }

    private async Task BackupDatabaseAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "备份数据库",
            Filter = "SQLite 数据库 (*.db)|*.db",
            FileName = $"app_usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db"
        };
        if (dialog.ShowDialog() != true) return;
        trackingService.Stop();
        await RunDataOperationAsync("正在备份数据库...", () => databaseService.BackupDatabaseAsync(dialog.FileName));
    }

    private async Task RestoreDatabaseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "恢复数据库",
            Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        var confirm = System.Windows.MessageBox.Show("恢复数据库会替换当前数据，是否继续？", "恢复数据库", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        trackingService.Stop();
        await RunDataOperationAsync("正在恢复数据库...", () => databaseService.RestoreDatabaseAsync(dialog.FileName));
        trackingService.ResetAfterDataClear();
        RefreshTodayDashboard();
        await RefreshDatabaseInfoAsync();
    }

    private async Task ClearAllDataAsync()
    {
        var confirm = System.Windows.MessageBox.Show("确定清空全部数据吗？此操作不可恢复。", "清空全部数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        trackingService.Stop();
        await RunDataOperationAsync("正在清空全部数据...", databaseService.ClearAllDataAsync);
        trackingService.ResetAfterDataClear();
        RefreshTodayDashboard();
        await RefreshDatabaseInfoAsync();
    }

    private async Task DeleteDataBeforeAsync(int days)
    {
        var confirm = System.Windows.MessageBox.Show($"确定删除 {days} 天前的记录吗？", "清理旧数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        await RunDataOperationAsync($"正在删除 {days} 天前的记录...", () => databaseService.DeleteUsageBeforeAsync(DateTime.Today.AddDays(-days)));
        await RefreshDatabaseInfoAsync();
    }

    private async Task RunHealthCheckAsync()
    {
        try
        {
            IsDataOperationRunning = true;
            DataStatusText = "正在执行健康检查...";
            DataHealthCheckResult = await databaseService.RunHealthCheckAsync();
            DataStatusText = "健康检查完成";
        }
        catch (Exception ex)
        {
            DataStatusText = "健康检查失败：" + ex.Message;
        }
        finally
        {
            IsDataOperationRunning = false;
        }
    }

    private async Task RunDataOperationAsync(string loadingText, Func<Task> operation)
    {
        try
        {
            IsDataOperationRunning = true;
            DataStatusText = loadingText;
            await operation();
            DataStatusText = "操作成功";
            await RefreshDatabaseInfoAsync();
        }
        catch (Exception ex)
        {
            DataStatusText = "操作失败：" + ex.Message;
        }
        finally
        {
            IsDataOperationRunning = false;
        }
    }

    private void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            LogService.Info("Opened directory: " + path);
        }
        catch (Exception ex)
        {
            LogService.Exception("Open directory failed: " + path, ex);
            DataStatusText = "打开目录失败：" + ex.Message;
        }
    }

    private void ExportCsv()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出今日使用记录",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"app-usage-{DateTime.Today:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        csvExportService.Export(dialog.FileName, databaseService.GetTodaySessionsForExport());
        StatusText = "CSV 已导出：" + dialog.FileName;
    }

    private void SetHistoryRange(string value)
    {
        currentHistoryRange = value switch
        {
            "Last7Days" => HistoryRange.Last7Days,
            "Last30Days" => HistoryRange.Last30Days,
            "All" => HistoryRange.All,
            _ => HistoryRange.Today
        };
        RefreshHistoryDashboard();
    }

    private void SetReportRange(string value)
    {
        currentReportRange = value switch
        {
            "LastWeek" => ReportRange.LastWeek,
            "ThisMonth" => ReportRange.ThisMonth,
            "LastMonth" => ReportRange.LastMonth,
            "Last7Days" => ReportRange.Last7Days,
            "Last30Days" => ReportRange.Last30Days,
            _ => ReportRange.ThisWeek
        };
        RefreshReportDashboard();
    }

    private void AddLiveSessionToToday(List<AppUsageStat> ranking, List<TimelineItem> timeline)
    {
        if (!TryGetLiveSession(out var live)) return;

        AddLiveUsage(ranking, live);

        if (live.DurationSeconds >= 3)
        {
            timeline.Insert(0, new TimelineItem
            {
                StartTime = live.StartTime,
                EndTime = null,
                AppName = live.DisplayName,
                ProcessName = live.ProcessName,
                DurationSeconds = live.DurationSeconds
            });
        }
    }

    private void ApplyTimelineDisplayTimes(IEnumerable<TimelineItem> timeline)
    {
        foreach (var item in timeline)
        {
            var start = timeZoneService.FormatDisplayTime(item.StartTime);
            var end = item.EndTime is null
                ? GetTimelineEndFallbackText(item)
                : timeZoneService.FormatDisplayTime(item.EndTime.Value);
            item.DisplayTimeRange = $"{start} - {end}";
        }
    }

    private static string GetTimelineEndFallbackText(TimelineItem item)
    {
        var separatorIndex = item.TimeRange.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0 ? item.TimeRange[(separatorIndex + 3)..] : "";
    }

    private void AddLiveSessionToHistory(List<AppUsageStat> stats, DateTime? start, DateTime? end)
    {
        if (!TryGetLiveSession(out var live)) return;
        if (start is not null && live.StartTime < start.Value) return;
        if (end is not null && live.StartTime >= end.Value) return;
        AddLiveUsage(stats, live);
    }

    private void AddLiveSessionToMultiRange(List<AppUsageMultiRangeStat> stats)
    {
        if (!TryGetLiveSession(out var live)) return;

        var existing = stats.FirstOrDefault(item =>
            string.Equals(item.ProcessName, live.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            stats.Add(new AppUsageMultiRangeStat
            {
                DisplayName = live.DisplayName,
                ProcessName = live.ProcessName,
                TodayDurationSeconds = live.DurationSeconds,
                Last7DaysDurationSeconds = live.DurationSeconds,
                Last30DaysDurationSeconds = live.DurationSeconds,
                TotalDurationSeconds = live.DurationSeconds
            });
            return;
        }

        existing.TodayDurationSeconds += live.DurationSeconds;
        existing.Last7DaysDurationSeconds += live.DurationSeconds;
        existing.Last30DaysDurationSeconds += live.DurationSeconds;
        existing.TotalDurationSeconds += live.DurationSeconds;
    }

    private bool TryGetLiveSession(out LiveSession live)
    {
        live = new LiveSession("", "", DateTime.MinValue, 0);
        if (trackingService.CurrentSessionStart is null || trackingService.CurrentDurationSeconds <= 0) return false;
        if (trackingService.CurrentSessionStart.Value.Date != DateTime.Today) return false;

        live = new LiveSession(
            trackingService.CurrentAppName,
            trackingService.CurrentProcessName,
            trackingService.CurrentSessionStart.Value,
            trackingService.CurrentDurationSeconds);
        return true;
    }

    private static void AddLiveUsage(List<AppUsageStat> stats, LiveSession live)
    {
        var existing = stats.FirstOrDefault(item =>
            string.Equals(item.ProcessName, live.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            stats.Add(new AppUsageStat
            {
                DisplayName = live.DisplayName,
                ProcessName = live.ProcessName,
                RangeDurationSeconds = live.DurationSeconds,
                TodayDurationSeconds = live.DurationSeconds,
                TotalDurationSeconds = live.DurationSeconds
            });
            return;
        }

        existing.RangeDurationSeconds += live.DurationSeconds;
        existing.TodayDurationSeconds += live.DurationSeconds;
        existing.TotalDurationSeconds += live.DurationSeconds;
    }

    private List<DailyUsageStat> BuildHistoryTrend(ResolvedRange range)
    {
        var trendStart = currentHistoryRange == HistoryRange.Last30Days
            ? DateTime.Today.AddDays(-29)
            : currentHistoryRange == HistoryRange.Last7Days
                ? DateTime.Today.AddDays(-6)
                : GetRangeStart(range);
        var trendEnd = currentHistoryRange is HistoryRange.Last30Days or HistoryRange.Last7Days
            ? DateTime.Today.AddDays(1)
            : GetRangeEnd(range);

        var trend = databaseService.GetDailyUsageTrendAsync(trendStart, trendEnd).GetAwaiter().GetResult();
        AddLiveSessionToTrend(trend, trendStart, trendEnd);
        return trend;
    }

    private void AddLiveSessionToTrend(List<DailyUsageStat> trend, DateTime start, DateTime end)
    {
        if (TryGetLiveSession(out var live) && live.StartTime >= start && live.StartTime < end)
        {
            var day = trend.FirstOrDefault(item => item.Date == DateTime.Today);
            if (day is not null)
            {
                day.DurationSeconds += live.DurationSeconds;
            }
        }

        var max = Math.Max(1, trend.Count == 0 ? 0 : trend.Max(item => item.DurationSeconds));
        foreach (var item in trend)
        {
            item.Percent = item.DurationSeconds * 100.0 / max;
        }
    }

    private static void ApplyReportTrendHeights(List<DailyUsageStat> trend)
    {
        var max = Math.Max(1, trend.Count == 0 ? 0 : trend.Max(item => item.DurationSeconds));
        foreach (var item in trend)
        {
            item.Percent = item.DurationSeconds <= 0
                ? 4
                : Math.Max(12, item.DurationSeconds * 180.0 / max);
        }
    }

    private static DateTime GetRangeStart(ResolvedRange range)
    {
        return range.Start ?? DateTime.MinValue;
    }

    private static DateTime GetRangeEnd(ResolvedRange range)
    {
        return range.End ?? DateTime.Today.AddDays(1);
    }

    private static string BuildReportSummaryText(
        ReportSummary summary,
        IReadOnlyList<UsageShareItem> topApps,
        IReadOnlyList<UsageShareItem> topCategories,
        IReadOnlyList<DailyUsageStat> trend,
        int completedFocusSessionCount,
        int totalFocusDurationSeconds,
        int averageFocusDurationSeconds,
        int distractionCount)
    {
        if (summary.TotalDurationSeconds <= 0)
        {
            return "\u6682\u65e0\u8db3\u591f\u6570\u636e\u751f\u6210\u603b\u7ed3\u3002";
        }

        var topApp = topApps.FirstOrDefault();
        var topCategory = topCategories.FirstOrDefault();
        var maxDay = trend.OrderByDescending(item => item.DurationSeconds).FirstOrDefault();

        if (topApp is null || topCategory is null || maxDay is null || maxDay.DurationSeconds <= 0)
        {
            return "\u6682\u65e0\u8db3\u591f\u6570\u636e\u751f\u6210\u603b\u7ed3\u3002";
        }

        return $"\u672c\u65f6\u95f4\u8303\u56f4\u603b\u4f7f\u7528\u7535\u8111 {summary.TotalDurationText}\uff0c\u5e73\u5747\u6bcf\u5929 {summary.AverageDailyDurationText}\u3002\n"
            + $"\u4f7f\u7528\u6700\u591a\u7684\u5e94\u7528\u662f {topApp.DisplayName}\uff0c\u5360\u603b\u65f6\u95f4\u7684 {topApp.PercentageText}\u3002\n"
            + $"\u6700\u5e38\u7528\u5206\u7c7b\u662f {topCategory.DisplayName}\uff0c\u7d2f\u8ba1 {topCategory.DurationText}\u3002\n"
            + $"\u5728 {maxDay.Date:MM\u6708dd\u65e5} \u4f7f\u7528\u65f6\u95f4\u6700\u957f\uff0c\u8fbe\u5230 {maxDay.DurationText}\u3002\n"
            + $"\u672c\u65f6\u95f4\u8303\u56f4\u5b8c\u6210\u4e86 {completedFocusSessionCount} \u6b21\u4e13\u6ce8\u4f1a\u8bdd\uff0c\u7d2f\u8ba1\u4e13\u6ce8 {TimeFormatter.Format(totalFocusDurationSeconds)}\uff0c\u5e73\u5747\u5355\u6b21 {TimeFormatter.Format(averageFocusDurationSeconds)}\uff0c\u5206\u5fc3 {distractionCount} \u6b21\u3002";
    }

    private List<DailyUsageStat> BuildTrend()
    {
        var trendStart = currentHistoryRange == HistoryRange.Last30Days ? DateTime.Today.AddDays(-29) : DateTime.Today.AddDays(-6);
        var trendEnd = DateTime.Today.AddDays(1);
        var trend = databaseService.GetDailyUsageStats(trendStart, trendEnd);

        if (TryGetLiveSession(out var live) && live.StartTime >= trendStart && live.StartTime < trendEnd)
        {
            var today = trend.FirstOrDefault(item => item.Date == DateTime.Today);
            if (today is not null) today.DurationSeconds += live.DurationSeconds;
        }

        var max = Math.Max(1, trend.Max(item => item.DurationSeconds));
        foreach (var item in trend)
        {
            item.Percent = item.DurationSeconds * 100.0 / max;
        }

        return trend;
    }

    private static int GetAverageDailySeconds(int totalSeconds, ResolvedRange range)
    {
        if (range.Start is null || range.End is null) return totalSeconds;
        var days = Math.Max(1, (int)Math.Ceiling((range.End.Value.Date - range.Start.Value.Date).TotalDays));
        return totalSeconds / days;
    }

    private ResolvedRange ResolveHistoryRange(HistoryRange range)
    {
        var today = timeZoneService.GetNowInSelectedTimeZone().Date;
        return range switch
        {
            HistoryRange.Last7Days => new ResolvedRange(today.AddDays(-6), today.AddDays(1), "\u6700\u8fd17\u5929"),
            HistoryRange.Last30Days => new ResolvedRange(today.AddDays(-29), today.AddDays(1), "\u6700\u8fd130\u5929"),
            HistoryRange.All => new ResolvedRange(null, null, "\u5168\u90e8\u5386\u53f2"),
            _ => new ResolvedRange(today, today.AddDays(1), "\u4eca\u5929")
        };
    }

    private ResolvedReportRange ResolveReportRange(ReportRange range)
    {
        var today = timeZoneService.GetNowInSelectedTimeZone().Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        return range switch
        {
            ReportRange.LastWeek => new ResolvedReportRange(weekStart.AddDays(-7), weekStart, "\u4e0a\u5468"),
            ReportRange.ThisMonth => new ResolvedReportRange(monthStart, monthStart.AddMonths(1), "\u672c\u6708"),
            ReportRange.LastMonth => new ResolvedReportRange(monthStart.AddMonths(-1), monthStart, "\u4e0a\u6708"),
            ReportRange.Last7Days => new ResolvedReportRange(today.AddDays(-6), today.AddDays(1), "\u6700\u8fd17\u5929"),
            ReportRange.Last30Days => new ResolvedReportRange(today.AddDays(-29), today.AddDays(1), "\u6700\u8fd130\u5929"),
            _ => new ResolvedReportRange(weekStart, weekStart.AddDays(7), "\u672c\u5468")
        };
    }

    private static void AssignRanks(IReadOnlyList<AppUsageStat> ranking)
    {
        for (var i = 0; i < ranking.Count; i++) ranking[i].Rank = i + 1;
    }

    private static void AssignRanks(IReadOnlyList<AppUsageMultiRangeStat> ranking)
    {
        for (var i = 0; i < ranking.Count; i++) ranking[i].Rank = i + 1;
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values) collection.Add(value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private enum HistoryRange { Today, Last7Days, Last30Days, All }

    private enum ReportRange { ThisWeek, LastWeek, ThisMonth, LastMonth, Last7Days, Last30Days }

    private sealed record ResolvedRange(DateTime? Start, DateTime? End, string Title);

    private sealed record ResolvedReportRange(DateTime Start, DateTime End, string Title);

    private sealed record LiveSession(string DisplayName, string ProcessName, DateTime StartTime, int DurationSeconds);
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Predicate<object?>? canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record SelectOption(string Value, string Label);
