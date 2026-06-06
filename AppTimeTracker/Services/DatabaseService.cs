using System.IO;
using System.Text;
using System.Text.Json;
using AppTimeTracker.Models;
using Microsoft.Data.Sqlite;

namespace AppTimeTracker.Services;

public sealed class DatabaseService
{
    private const string SelfProcessName = "AppTimeTracker.exe";
    private readonly string databasePath;
    private readonly string connectionString;

    public DatabaseService()
    {
        AppPaths.EnsureDirectories();
        AppPaths.MigrateLegacyDatabaseIfNeeded();
        databasePath = AppPaths.DatabasePath;
        connectionString = $"Data Source={databasePath}";
        Initialize();
    }

    public string DatabasePath => databasePath;

    public void Initialize()
    {
        LogService.Info("Database initialization started: " + databasePath);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS apps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                process_name TEXT NOT NULL UNIQUE,
                display_name TEXT,
                category TEXT,
                is_ignored INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS usage_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id INTEGER NOT NULL,
                window_title TEXT,
                start_time TEXT NOT NULL,
                end_time TEXT,
                duration_seconds INTEGER DEFAULT 0,
                FOREIGN KEY(app_id) REFERENCES apps(id)
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS focus_rules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                target_type TEXT NOT NULL,
                target_value TEXT NOT NULL,
                rule_type TEXT NOT NULL,
                limit_seconds INTEGER NOT NULL,
                is_enabled INTEGER DEFAULT 1,
                notify_enabled INTEGER DEFAULT 1,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS focus_rule_notifications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                rule_id INTEGER NOT NULL,
                notify_date TEXT NOT NULL,
                notified_at TEXT NOT NULL,
                FOREIGN KEY(rule_id) REFERENCES focus_rules(id)
            );

            CREATE TABLE IF NOT EXISTS focus_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                target_type TEXT NOT NULL,
                target_value TEXT NOT NULL,
                planned_seconds INTEGER NOT NULL,
                actual_seconds INTEGER DEFAULT 0,
                distraction_count INTEGER DEFAULT 0,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT
            );

            CREATE TABLE IF NOT EXISTS focus_distractions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                process_name TEXT NOT NULL,
                display_name TEXT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                duration_seconds INTEGER DEFAULT 0,
                FOREIGN KEY(session_id) REFERENCES focus_sessions(id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS idx_focus_rule_notifications_day
            ON focus_rule_notifications(rule_id, notify_date);

            CREATE INDEX IF NOT EXISTS idx_usage_sessions_start_time
            ON usage_sessions(start_time);

            CREATE INDEX IF NOT EXISTS idx_usage_sessions_app_id
            ON usage_sessions(app_id);

            CREATE INDEX IF NOT EXISTS idx_usage_sessions_app_time
            ON usage_sessions(app_id, start_time);

            CREATE INDEX IF NOT EXISTS idx_apps_process_name
            ON apps(process_name);

            CREATE INDEX IF NOT EXISTS idx_apps_is_ignored
            ON apps(is_ignored);

            CREATE INDEX IF NOT EXISTS idx_focus_sessions_started_at
            ON focus_sessions(started_at);

            CREATE INDEX IF NOT EXISTS idx_focus_distractions_session_id
            ON focus_distractions(session_id);
            """;
        command.ExecuteNonQuery();
        EnsureAppColumns(connection);
        EnsureSelfIgnored();
        LogService.Info("Database initialization completed");
    }

    public int GetOrCreateApp(string processName, string displayName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO apps(process_name, display_name)
            VALUES($process_name, $display_name)
            ON CONFLICT(process_name) DO UPDATE SET
                display_name = COALESCE(NULLIF($display_name, ''), apps.display_name)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$process_name", processName);
        command.Parameters.AddWithValue("$display_name", displayName);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int StartSession(int appId, string? windowTitle, DateTime startTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO usage_sessions(app_id, window_title, start_time)
            VALUES($app_id, $window_title, $start_time)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$app_id", appId);
        command.Parameters.AddWithValue("$window_title", (object?)windowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$start_time", ToDbTime(startTime));

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void EndSession(int sessionId, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE usage_sessions
            SET end_time = $end_time,
                duration_seconds = MAX(0, CAST((julianday($end_time) - julianday(start_time)) * 86400 AS INTEGER))
            WHERE id = $id AND end_time IS NULL;
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$end_time", ToDbTime(endTime));
        command.ExecuteNonQuery();
    }

    public async Task<bool> IsAppIgnoredAsync(string processName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(is_ignored, 0)
            FROM apps
            WHERE process_name = $process_name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$process_name", processName);
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is not null && value != DBNull.Value && Convert.ToInt32(value) != 0;
    }

    public string GetSetting(string key, string defaultValue)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT value
            FROM settings
            WHERE key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", key);

        var value = command.ExecuteScalar();
        return value is null || value == DBNull.Value ? defaultValue : Convert.ToString(value) ?? defaultValue;
    }

    public bool GetBoolSetting(string key, bool defaultValue)
    {
        var value = GetSetting(key, defaultValue ? "true" : "false");
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value)
            VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public void SetBoolSetting(string key, bool value)
    {
        SetSetting(key, value ? "true" : "false");
    }

    public List<AppUsageStat> GetTodayUsageStats()
    {
        var today = DateTime.Today;
        return GetUsageStats(today, today.AddDays(1));
    }

    public List<AppUsageStat> GetUsageStats(DateTime? startTime, DateTime? endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var where = BuildRangeWhere(command, startTime, endTime, excludeIgnored: true);
        command.CommandText = $"""
            SELECT
                a.id,
                COALESCE(a.display_name, a.process_name) AS display_name,
                a.process_name,
                COALESCE(SUM(s.duration_seconds), 0) AS duration_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            {where}
            GROUP BY a.id, a.display_name, a.process_name
            HAVING duration_seconds > 0
            ORDER BY duration_seconds DESC;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<AppUsageStat>();
        while (reader.Read())
        {
            result.Add(new AppUsageStat
            {
                AppId = reader.GetInt32(0),
                DisplayName = reader.GetString(1),
                ProcessName = reader.GetString(2),
                RangeDurationSeconds = reader.GetInt32(3),
                TodayDurationSeconds = reader.GetInt32(3)
            });
        }

        return result;
    }

    public async Task<List<AppUsageMultiRangeStat>> GetAppUsageMultiRangeStatsAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        AddRangeParameters(command);
        command.CommandText = """
            SELECT
                COALESCE(a.display_name, a.process_name) AS display_name,
                a.process_name,
                COALESCE(SUM(CASE WHEN s.start_time >= $today_start AND s.start_time < $tomorrow_start THEN s.duration_seconds ELSE 0 END), 0) AS today_seconds,
                COALESCE(SUM(CASE WHEN s.start_time >= $last7_start AND s.start_time < $tomorrow_start THEN s.duration_seconds ELSE 0 END), 0) AS last7_seconds,
                COALESCE(SUM(CASE WHEN s.start_time >= $last30_start AND s.start_time < $tomorrow_start THEN s.duration_seconds ELSE 0 END), 0) AS last30_seconds,
                COALESCE(SUM(s.duration_seconds), 0) AS total_seconds
            FROM apps a
            LEFT JOIN usage_sessions s ON s.app_id = a.id
            WHERE COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            GROUP BY a.id, a.display_name, a.process_name
            HAVING total_seconds > 0 OR today_seconds > 0 OR last7_seconds > 0 OR last30_seconds > 0
            ORDER BY last7_seconds DESC, total_seconds DESC;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<AppUsageMultiRangeStat>();
        while (reader.Read())
        {
            result.Add(new AppUsageMultiRangeStat
            {
                DisplayName = reader.GetString(0),
                ProcessName = reader.GetString(1),
                TodayDurationSeconds = reader.GetInt32(2),
                Last7DaysDurationSeconds = reader.GetInt32(3),
                Last30DaysDurationSeconds = reader.GetInt32(4),
                TotalDurationSeconds = reader.GetInt32(5)
            });
        }

        await Task.CompletedTask;
        return result;
    }

    public Dictionary<int, int> GetTotalUsageSecondsByApp()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.app_id, COALESCE(SUM(s.duration_seconds), 0) AS total_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            GROUP BY s.app_id;
            """;
        command.Parameters.AddWithValue("$self_process", SelfProcessName);

        using var reader = command.ExecuteReader();
        var result = new Dictionary<int, int>();
        while (reader.Read())
        {
            result[reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return result;
    }

    public async Task<List<ManagedAppInfo>> GetAllAppsWithStatsAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        AddRangeParameters(command);
        command.CommandText = """
            SELECT
                a.id,
                a.process_name,
                COALESCE(a.display_name, a.process_name) AS display_name,
                COALESCE(NULLIF(a.category, ''), '其他') AS category,
                COALESCE(a.is_ignored, 0) AS is_ignored,
                COALESCE(SUM(CASE WHEN s.start_time >= $today_start AND s.start_time < $tomorrow_start THEN s.duration_seconds ELSE 0 END), 0) AS today_seconds,
                COALESCE(SUM(CASE WHEN s.start_time >= $last7_start AND s.start_time < $tomorrow_start THEN s.duration_seconds ELSE 0 END), 0) AS last7_seconds,
                COALESCE(SUM(CASE WHEN s.start_time >= $last30_start AND s.start_time < $tomorrow_start THEN s.duration_seconds ELSE 0 END), 0) AS last30_seconds,
                COALESCE(SUM(s.duration_seconds), 0) AS total_seconds
            FROM apps a
            LEFT JOIN usage_sessions s ON s.app_id = a.id
            GROUP BY a.id, a.process_name, a.display_name, a.category, a.is_ignored
            ORDER BY total_seconds DESC, display_name;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<ManagedAppInfo>();
        while (reader.Read())
        {
            result.Add(new ManagedAppInfo
            {
                Id = reader.GetInt32(0),
                ProcessName = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Category = reader.GetString(3),
                IsIgnored = reader.GetInt32(4) != 0,
                TodayDurationSeconds = reader.GetInt32(5),
                Last7DaysDurationSeconds = reader.GetInt32(6),
                Last30DaysDurationSeconds = reader.GetInt32(7),
                TotalDurationSeconds = reader.GetInt32(8)
            });
        }

        await Task.CompletedTask;
        return result;
    }

    public async Task UpdateAppDisplayNameAsync(string processName, string displayName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE apps
            SET display_name = $display_name
            WHERE process_name = $process_name;
            """;
        command.Parameters.AddWithValue("$process_name", processName);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task UpdateAppCategoryAsync(string processName, string category)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE apps
            SET category = $category
            WHERE process_name = $process_name;
            """;
        command.Parameters.AddWithValue("$process_name", processName);
        command.Parameters.AddWithValue("$category", category);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task UpdateAppIgnoredAsync(string processName, bool isIgnored)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE apps
            SET is_ignored = $is_ignored
            WHERE process_name = $process_name;
            """;
        command.Parameters.AddWithValue("$process_name", processName);
        command.Parameters.AddWithValue("$is_ignored", isIgnored ? 1 : 0);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public List<TimelineItem> GetTodayTimeline()
    {
        var today = DateTime.Today;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.start_time,
                s.end_time,
                s.duration_seconds,
                COALESCE(a.display_name, a.process_name) AS app_name,
                a.process_name
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND s.duration_seconds >= 3
                AND COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            ORDER BY s.start_time DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(today));
        command.Parameters.AddWithValue("$end", ToDbTime(today.AddDays(1)));
        command.Parameters.AddWithValue("$self_process", SelfProcessName);

        using var reader = command.ExecuteReader();
        var result = new List<TimelineItem>();
        while (reader.Read())
        {
            result.Add(new TimelineItem
            {
                StartTime = FromDbTime(reader.GetString(0)),
                EndTime = reader.IsDBNull(1) ? null : FromDbTime(reader.GetString(1)),
                DurationSeconds = reader.GetInt32(2),
                AppName = reader.GetString(3),
                ProcessName = reader.GetString(4)
            });
        }

        return result;
    }

    public void ClearTodayData()
    {
        var today = DateTime.Today;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM usage_sessions
            WHERE start_time >= $start AND start_time < $end;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(today));
        command.Parameters.AddWithValue("$end", ToDbTime(today.AddDays(1)));
        command.ExecuteNonQuery();
    }

    public List<DailyUsageStat> GetDailyUsageStats(DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT substr(s.start_time, 1, 10) AS day, COALESCE(SUM(s.duration_seconds), 0) AS duration_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            GROUP BY day
            ORDER BY day;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(startTime));
        command.Parameters.AddWithValue("$end", ToDbTime(endTime));
        command.Parameters.AddWithValue("$self_process", SelfProcessName);

        using var reader = command.ExecuteReader();
        var byDate = new Dictionary<DateTime, int>();
        while (reader.Read())
        {
            byDate[DateTime.Parse(reader.GetString(0))] = reader.GetInt32(1);
        }

        var result = new List<DailyUsageStat>();
        for (var day = startTime.Date; day < endTime.Date; day = day.AddDays(1))
        {
            result.Add(new DailyUsageStat
            {
                Date = day,
                DurationSeconds = byDate.GetValueOrDefault(day)
            });
        }

        var max = Math.Max(1, result.Max(item => item.DurationSeconds));
        foreach (var item in result)
        {
            item.Percent = item.DurationSeconds * 100.0 / max;
        }

        return result;
    }

    public List<UsageSession> GetTodaySessionsForExport()
    {
        var today = DateTime.Today;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.id,
                s.app_id,
                a.process_name,
                COALESCE(a.display_name, a.process_name) AS display_name,
                s.window_title,
                s.start_time,
                s.end_time,
                s.duration_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
            ORDER BY s.start_time;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(today));
        command.Parameters.AddWithValue("$end", ToDbTime(today.AddDays(1)));

        using var reader = command.ExecuteReader();
        var result = new List<UsageSession>();
        while (reader.Read())
        {
            result.Add(new UsageSession
            {
                Id = reader.GetInt32(0),
                AppId = reader.GetInt32(1),
                ProcessName = reader.GetString(2),
                DisplayName = reader.GetString(3),
                WindowTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                StartTime = FromDbTime(reader.GetString(5)),
                EndTime = reader.IsDBNull(6) ? null : FromDbTime(reader.GetString(6)),
                DurationSeconds = reader.GetInt32(7)
            });
        }

        return result;
    }

    public async Task<List<FocusRule>> GetFocusRulesAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, target_type, target_value, rule_type, limit_seconds, is_enabled, notify_enabled, created_at
            FROM focus_rules
            ORDER BY is_enabled DESC, created_at DESC;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<FocusRule>();
        while (reader.Read())
        {
            result.Add(new FocusRule
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                TargetType = reader.GetString(2),
                TargetValue = reader.GetString(3),
                RuleType = reader.GetString(4),
                LimitSeconds = reader.GetInt32(5),
                IsEnabled = reader.GetInt32(6) != 0,
                NotifyEnabled = reader.GetInt32(7) != 0,
                CreatedAt = FromDbTime(reader.GetString(8))
            });
        }

        await Task.CompletedTask;
        return result;
    }

    public async Task<int> AddFocusRuleAsync(FocusRule rule)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO focus_rules(name, target_type, target_value, rule_type, limit_seconds, is_enabled, notify_enabled, created_at)
            VALUES($name, $target_type, $target_value, $rule_type, $limit_seconds, $is_enabled, $notify_enabled, $created_at)
            RETURNING id;
            """;
        AddFocusRuleParameters(command, rule);
        var id = Convert.ToInt32(command.ExecuteScalar());
        await Task.CompletedTask;
        return id;
    }

    public async Task UpdateFocusRuleAsync(FocusRule rule)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE focus_rules
            SET name = $name,
                target_type = $target_type,
                target_value = $target_value,
                rule_type = $rule_type,
                limit_seconds = $limit_seconds,
                is_enabled = $is_enabled,
                notify_enabled = $notify_enabled
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", rule.Id);
        AddFocusRuleParameters(command, rule);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task DeleteFocusRuleAsync(int ruleId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM focus_rule_notifications WHERE rule_id = $id;
            DELETE FROM focus_rules WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", ruleId);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task SetFocusRuleEnabledAsync(int ruleId, bool isEnabled)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE focus_rules
            SET is_enabled = $is_enabled
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", ruleId);
        command.Parameters.AddWithValue("$is_enabled", isEnabled ? 1 : 0);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task<bool> HasRuleNotifiedTodayAsync(int ruleId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM focus_rule_notifications
            WHERE rule_id = $rule_id AND notify_date = $notify_date
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$rule_id", ruleId);
        command.Parameters.AddWithValue("$notify_date", DateTime.Today.ToString("yyyy-MM-dd"));
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is not null && value != DBNull.Value;
    }

    public async Task MarkRuleNotifiedTodayAsync(int ruleId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO focus_rule_notifications(rule_id, notify_date, notified_at)
            VALUES($rule_id, $notify_date, $notified_at);
            """;
        command.Parameters.AddWithValue("$rule_id", ruleId);
        command.Parameters.AddWithValue("$notify_date", DateTime.Today.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$notified_at", ToDbTime(DateTime.Now));
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task<int> GetTodayUsageByProcessNameAsync(string processName)
    {
        var today = DateTime.Today;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(s.duration_seconds), 0)
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND a.process_name = $process_name
                AND COALESCE(a.is_ignored, 0) = 0;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(today));
        command.Parameters.AddWithValue("$end", ToDbTime(today.AddDays(1)));
        command.Parameters.AddWithValue("$process_name", processName);
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    public async Task<int> GetTodayUsageByCategoryAsync(string category)
    {
        var today = DateTime.Today;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(s.duration_seconds), 0)
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND COALESCE(NULLIF(a.category, ''), '其他') = $category
                AND COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(today));
        command.Parameters.AddWithValue("$end", ToDbTime(today.AddDays(1)));
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$self_process", SelfProcessName);
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    public async Task<int> GetTodayFocusNotificationCountAsync()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM focus_rule_notifications
            WHERE notify_date = $notify_date;
            """;
        command.Parameters.AddWithValue("$notify_date", DateTime.Today.ToString("yyyy-MM-dd"));
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    public async Task<List<DailyUsageStat>> GetDailyUsageTrendAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT substr(s.start_time, 1, 10) AS day, COALESCE(SUM(s.duration_seconds), 0) AS duration_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND s.duration_seconds >= 3
                AND COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            GROUP BY day
            ORDER BY day;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(startTime));
        command.Parameters.AddWithValue("$end", ToDbTime(endTime));
        command.Parameters.AddWithValue("$self_process", SelfProcessName);

        using var reader = command.ExecuteReader();
        var byDate = new Dictionary<DateTime, int>();
        while (reader.Read())
        {
            byDate[DateTime.Parse(reader.GetString(0))] = reader.GetInt32(1);
        }

        var result = new List<DailyUsageStat>();
        for (var day = startTime.Date; day < endTime.Date; day = day.AddDays(1))
        {
            result.Add(new DailyUsageStat
            {
                Date = day,
                DurationSeconds = byDate.GetValueOrDefault(day)
            });
        }

        ApplyDailyPercent(result);
        await Task.CompletedTask;
        return result;
    }

    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        return await Task.Run(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM usage_sessions),
                    (SELECT COUNT(*) FROM apps),
                    (SELECT MIN(start_time) FROM usage_sessions),
                    (SELECT MAX(start_time) FROM usage_sessions);
                """;

            using var reader = command.ExecuteReader();
            reader.Read();
            return new DatabaseInfo
            {
                DatabasePath = databasePath,
                FileSizeBytes = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0,
                UsageSessionCount = reader.GetInt32(0),
                AppCount = reader.GetInt32(1),
                EarliestRecordTime = reader.IsDBNull(2) ? null : FromDbTime(reader.GetString(2)),
                LatestRecordTime = reader.IsDBNull(3) ? null : FromDbTime(reader.GetString(3))
            };
        });
    }

    public async Task ExportJsonAsync(string filePath)
    {
        await Task.Run(() =>
        {
            using var connection = OpenConnection();
            var payload = new Dictionary<string, object?>
            {
                ["apps"] = ReadTable(connection, "SELECT * FROM apps ORDER BY id;"),
                ["usage_sessions"] = ReadTable(connection, "SELECT * FROM usage_sessions ORDER BY start_time;"),
                ["focus_rules"] = ReadTable(connection, "SELECT * FROM focus_rules ORDER BY id;"),
                ["focus_sessions"] = ReadTable(connection, "SELECT * FROM focus_sessions ORDER BY started_at;"),
                ["focus_distractions"] = ReadTable(connection, "SELECT * FROM focus_distractions ORDER BY started_at;"),
                ["settings"] = ReadTable(connection, "SELECT * FROM settings ORDER BY key;")
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
        });
    }

    public async Task ExportReportTextAsync(string filePath)
    {
        await Task.Run(async () =>
        {
            var start = DateTime.Today.AddDays(-6);
            var end = DateTime.Today.AddDays(1);
            var summary = await GetReportSummaryAsync(start, end);
            var topApps = await GetTopAppsAsync(start, end, 5);
            var topCategories = await GetTopCategoriesAsync(start, end, 5);
            var builder = new StringBuilder();
            builder.AppendLine("time-controller 报告摘要");
            builder.AppendLine($"范围：{start:yyyy-MM-dd} - {end.AddDays(-1):yyyy-MM-dd}");
            builder.AppendLine($"总使用时长：{summary.TotalDurationText}");
            builder.AppendLine($"平均每日使用：{summary.AverageDailyDurationText}");
            builder.AppendLine($"最常用应用：{summary.MostUsedApp}");
            builder.AppendLine($"最常用分类：{summary.MostUsedCategory}");
            builder.AppendLine();
            builder.AppendLine("Top 应用：");
            foreach (var item in topApps)
            {
                builder.AppendLine($"- {item.DisplayName} ({item.ProcessName}) {item.DurationText} {item.PercentageText}");
            }
            builder.AppendLine();
            builder.AppendLine("Top 分类：");
            foreach (var item in topCategories)
            {
                builder.AppendLine($"- {item.DisplayName} {item.DurationText} {item.PercentageText}");
            }

            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
        });
    }

    public async Task BackupDatabaseAsync(string destinationPath)
    {
        await Task.Run(() => File.Copy(databasePath, destinationPath, overwrite: true));
    }

    public async Task RestoreDatabaseAsync(string sourcePath)
    {
        await Task.Run(() =>
        {
            var safetyBackup = databasePath + ".restore_backup";
            if (File.Exists(databasePath))
            {
                File.Copy(databasePath, safetyBackup, overwrite: true);
            }

            try
            {
                File.Copy(sourcePath, databasePath, overwrite: true);
                Initialize();
            }
            catch
            {
                if (File.Exists(safetyBackup))
                {
                    File.Copy(safetyBackup, databasePath, overwrite: true);
                }

                throw;
            }
        });
    }

    public async Task ClearAllDataAsync()
    {
        await Task.Run(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM focus_rule_notifications;
                DELETE FROM focus_distractions;
                DELETE FROM focus_sessions;
                DELETE FROM usage_sessions;
                DELETE FROM focus_rules;
                DELETE FROM apps WHERE process_name <> $self_process;
                """;
            command.Parameters.AddWithValue("$self_process", SelfProcessName);
            command.ExecuteNonQuery();
            EnsureSelfIgnored();
        });
    }

    public async Task DeleteUsageBeforeAsync(DateTime cutoff)
    {
        await Task.Run(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM usage_sessions WHERE start_time < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", ToDbTime(cutoff));
            command.ExecuteNonQuery();
        });
    }

    public async Task VacuumAsync()
    {
        await Task.Run(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM;";
            command.ExecuteNonQuery();
        });
    }

    public async Task RebuildIndexesAsync()
    {
        await Task.Run(() =>
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "REINDEX;";
            command.ExecuteNonQuery();
        });
    }

    public async Task<string> RunHealthCheckAsync()
    {
        return await Task.Run(() =>
        {
            using var connection = OpenConnection();
            var orphan = ScalarInt(connection, """
                SELECT COUNT(*)
                FROM usage_sessions s
                LEFT JOIN apps a ON a.id = s.app_id
                WHERE a.id IS NULL;
                """);
            var negative = ScalarInt(connection, "SELECT COUNT(*) FROM usage_sessions WHERE duration_seconds < 0;");
            var openEnded = ScalarInt(connection, "SELECT COUNT(*) FROM usage_sessions WHERE end_time IS NULL;");
            return $"孤儿记录：{orphan}\n负数时长记录：{negative}\n未结束记录：{openEnded}";
        });
    }

    public async Task<AppInfo?> GetAppInfoByProcessNameAsync(string processName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, process_name, display_name, category, COALESCE(is_ignored, 0)
            FROM apps
            WHERE process_name = $process_name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$process_name", processName);
        using var reader = command.ExecuteReader();
        await Task.CompletedTask;
        if (!reader.Read())
        {
            return null;
        }

        return new AppInfo
        {
            Id = reader.GetInt32(0),
            ProcessName = reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Category = reader.IsDBNull(3) ? null : reader.GetString(3),
            IsIgnored = reader.GetInt32(4) != 0
        };
    }

    public async Task<int> CreateFocusSessionAsync(FocusSession session)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO focus_sessions(name, target_type, target_value, planned_seconds, actual_seconds, distraction_count, status, started_at, ended_at)
            VALUES($name, $target_type, $target_value, $planned_seconds, $actual_seconds, $distraction_count, $status, $started_at, $ended_at)
            RETURNING id;
            """;
        AddFocusSessionParameters(command, session);
        var id = Convert.ToInt32(command.ExecuteScalar());
        await Task.CompletedTask;
        return id;
    }

    public async Task UpdateFocusSessionAsync(FocusSession session)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE focus_sessions
            SET actual_seconds = $actual_seconds,
                distraction_count = $distraction_count,
                status = $status,
                ended_at = $ended_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$actual_seconds", session.ActualSeconds);
        command.Parameters.AddWithValue("$distraction_count", session.DistractionCount);
        command.Parameters.AddWithValue("$status", session.Status);
        command.Parameters.AddWithValue("$ended_at", (object?)ToNullableDbTime(session.EndedAt) ?? DBNull.Value);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task CompleteFocusSessionAsync(int sessionId, int actualSeconds, int distractionCount)
    {
        await FinishFocusSessionAsync(sessionId, actualSeconds, distractionCount, "completed");
    }

    public async Task AbandonFocusSessionAsync(int sessionId, int actualSeconds, int distractionCount)
    {
        await FinishFocusSessionAsync(sessionId, actualSeconds, distractionCount, "abandoned");
    }

    public async Task<List<FocusSession>> GetFocusSessionsAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, target_type, target_value, planned_seconds, actual_seconds, distraction_count, status, started_at, ended_at
            FROM focus_sessions
            WHERE started_at >= $start AND started_at < $end
            ORDER BY started_at DESC;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(startTime));
        command.Parameters.AddWithValue("$end", ToDbTime(endTime));
        using var reader = command.ExecuteReader();
        var result = new List<FocusSession>();
        while (reader.Read())
        {
            result.Add(ReadFocusSession(reader));
        }

        await Task.CompletedTask;
        return result;
    }

    public async Task<FocusTodaySummary> GetTodayFocusSummaryAsync()
    {
        var today = DateTime.Today;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN status = 'completed' THEN actual_seconds ELSE 0 END), 0),
                COALESCE(SUM(distraction_count), 0)
            FROM focus_sessions
            WHERE started_at >= $start AND started_at < $end;
            """;
        command.Parameters.AddWithValue("$start", ToDbTime(today));
        command.Parameters.AddWithValue("$end", ToDbTime(today.AddDays(1)));
        using var reader = command.ExecuteReader();
        reader.Read();
        await Task.CompletedTask;
        return new FocusTodaySummary
        {
            CompletedCount = reader.GetInt32(0),
            TotalFocusSeconds = reader.GetInt32(1),
            TotalDistractionCount = reader.GetInt32(2)
        };
    }

    public async Task<int> AddFocusDistractionAsync(FocusDistraction distraction)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO focus_distractions(session_id, process_name, display_name, started_at)
            VALUES($session_id, $process_name, $display_name, $started_at)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$session_id", distraction.SessionId);
        command.Parameters.AddWithValue("$process_name", distraction.ProcessName);
        command.Parameters.AddWithValue("$display_name", distraction.DisplayName);
        command.Parameters.AddWithValue("$started_at", ToDbTime(distraction.StartedAt));
        var id = Convert.ToInt32(command.ExecuteScalar());
        await Task.CompletedTask;
        return id;
    }

    public async Task EndFocusDistractionAsync(int distractionId, DateTime endTime, int durationSeconds)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE focus_distractions
            SET ended_at = $ended_at,
                duration_seconds = $duration_seconds
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", distractionId);
        command.Parameters.AddWithValue("$ended_at", ToDbTime(endTime));
        command.Parameters.AddWithValue("$duration_seconds", durationSeconds);
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    public async Task<List<FocusDistraction>> GetSessionDistractionsAsync(int sessionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_id, process_name, display_name, started_at, ended_at, duration_seconds
            FROM focus_distractions
            WHERE session_id = $session_id
            ORDER BY started_at DESC;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();
        var result = new List<FocusDistraction>();
        while (reader.Read())
        {
            result.Add(new FocusDistraction
            {
                Id = reader.GetInt32(0),
                SessionId = reader.GetInt32(1),
                ProcessName = reader.GetString(2),
                DisplayName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StartedAt = FromDbTime(reader.GetString(4)),
                EndedAt = reader.IsDBNull(5) ? null : FromDbTime(reader.GetString(5)),
                DurationSeconds = reader.GetInt32(6)
            });
        }

        await Task.CompletedTask;
        return result;
    }

    public async Task<int> GetCompletedFocusCountAsync(DateTime startTime, DateTime endTime)
    {
        return await ScalarIntAsync("SELECT COUNT(*) FROM focus_sessions WHERE status = 'completed' AND started_at >= $start AND started_at < $end;", startTime, endTime);
    }

    public async Task<int> GetTotalFocusSecondsAsync(DateTime startTime, DateTime endTime)
    {
        return await ScalarIntAsync("SELECT COALESCE(SUM(actual_seconds), 0) FROM focus_sessions WHERE status = 'completed' AND started_at >= $start AND started_at < $end;", startTime, endTime);
    }

    public async Task<int> GetTotalDistractionCountAsync(DateTime startTime, DateTime endTime)
    {
        return await ScalarIntAsync("SELECT COALESCE(SUM(distraction_count), 0) FROM focus_sessions WHERE started_at >= $start AND started_at < $end;", startTime, endTime);
    }

    public async Task<List<UsageShareItem>> GetAppUsageShareAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(a.display_name, a.process_name) AS display_name,
                a.process_name,
                COALESCE(SUM(s.duration_seconds), 0) AS duration_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND s.duration_seconds >= 3
                AND COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            GROUP BY a.id, a.display_name, a.process_name
            HAVING duration_seconds > 0
            ORDER BY duration_seconds DESC;
            """;
        AddRangeQueryParameters(command, startTime, endTime);

        using var reader = command.ExecuteReader();
        var result = new List<UsageShareItem>();
        while (reader.Read())
        {
            result.Add(new UsageShareItem
            {
                DisplayName = reader.GetString(0),
                ProcessName = reader.GetString(1),
                DurationSeconds = reader.GetInt32(2)
            });
        }

        ApplySharePercent(result);
        await Task.CompletedTask;
        return result;
    }

    public async Task<List<UsageShareItem>> GetCategoryUsageShareAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(NULLIF(a.category, ''), '其他') AS category,
                COALESCE(SUM(s.duration_seconds), 0) AS duration_seconds
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.start_time >= $start AND s.start_time < $end
                AND s.duration_seconds >= 3
                AND COALESCE(a.is_ignored, 0) = 0
                AND a.process_name <> $self_process
            GROUP BY category
            HAVING duration_seconds > 0
            ORDER BY duration_seconds DESC;
            """;
        AddRangeQueryParameters(command, startTime, endTime);

        using var reader = command.ExecuteReader();
        var result = new List<UsageShareItem>();
        while (reader.Read())
        {
            var category = reader.GetString(0);
            result.Add(new UsageShareItem
            {
                DisplayName = category,
                Category = category,
                DurationSeconds = reader.GetInt32(1)
            });
        }

        ApplySharePercent(result);
        await Task.CompletedTask;
        return result;
    }

    public async Task<ReportSummary> GetReportSummaryAsync(DateTime startTime, DateTime endTime)
    {
        var apps = await GetAppUsageShareAsync(startTime, endTime);
        var categories = await GetCategoryUsageShareAsync(startTime, endTime);
        var totalSeconds = apps.Sum(item => item.DurationSeconds);
        var days = Math.Max(1, (int)Math.Ceiling((endTime.Date - startTime.Date).TotalDays));

        return new ReportSummary
        {
            TotalDurationSeconds = totalSeconds,
            AverageDailyDurationSeconds = totalSeconds / days,
            MostUsedApp = apps.FirstOrDefault()?.DisplayName ?? "暂无",
            MostUsedCategory = categories.FirstOrDefault()?.DisplayName ?? "暂无",
            AppCount = apps.Count,
            RuleNotificationCount = await GetRuleNotificationCountAsync(startTime, endTime)
        };
    }

    public async Task<List<UsageShareItem>> GetTopAppsAsync(DateTime startTime, DateTime endTime, int limit)
    {
        return (await GetAppUsageShareAsync(startTime, endTime)).Take(Math.Max(1, limit)).ToList();
    }

    public async Task<List<UsageShareItem>> GetTopCategoriesAsync(DateTime startTime, DateTime endTime, int limit)
    {
        return (await GetCategoryUsageShareAsync(startTime, endTime)).Take(Math.Max(1, limit)).ToList();
    }

    private async Task<int> GetRuleNotificationCountAsync(DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM focus_rule_notifications
            WHERE notify_date >= $start_date AND notify_date < $end_date;
            """;
        command.Parameters.AddWithValue("$start_date", startTime.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end_date", endTime.Date.ToString("yyyy-MM-dd"));
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static List<Dictionary<string, object?>> ReadTable(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void EnsureAppColumns(SqliteConnection connection)
    {
        using var readCommand = connection.CreateCommand();
        readCommand.CommandText = "PRAGMA table_info(apps);";
        using var reader = readCommand.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("is_ignored"))
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE apps ADD COLUMN is_ignored INTEGER DEFAULT 0;";
            alterCommand.ExecuteNonQuery();
        }
    }

    private void EnsureSelfIgnored()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO apps(process_name, display_name, category, is_ignored)
            VALUES($process_name, $display_name, $category, 1)
            ON CONFLICT(process_name) DO UPDATE SET is_ignored = 1;
            """;
        command.Parameters.AddWithValue("$process_name", SelfProcessName);
        command.Parameters.AddWithValue("$display_name", "time-controller");
        command.Parameters.AddWithValue("$category", "系统工具");
        command.ExecuteNonQuery();
    }

    private static void AddFocusRuleParameters(SqliteCommand command, FocusRule rule)
    {
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$target_type", rule.TargetType);
        command.Parameters.AddWithValue("$target_value", rule.TargetValue);
        command.Parameters.AddWithValue("$rule_type", rule.RuleType);
        command.Parameters.AddWithValue("$limit_seconds", rule.LimitSeconds);
        command.Parameters.AddWithValue("$is_enabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$notify_enabled", rule.NotifyEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created_at", ToDbTime(rule.CreatedAt == default ? DateTime.Now : rule.CreatedAt));
    }

    private static void AddFocusSessionParameters(SqliteCommand command, FocusSession session)
    {
        command.Parameters.AddWithValue("$name", session.Name);
        command.Parameters.AddWithValue("$target_type", session.TargetType);
        command.Parameters.AddWithValue("$target_value", session.TargetValue);
        command.Parameters.AddWithValue("$planned_seconds", session.PlannedSeconds);
        command.Parameters.AddWithValue("$actual_seconds", session.ActualSeconds);
        command.Parameters.AddWithValue("$distraction_count", session.DistractionCount);
        command.Parameters.AddWithValue("$status", session.Status);
        command.Parameters.AddWithValue("$started_at", ToDbTime(session.StartedAt));
        command.Parameters.AddWithValue("$ended_at", (object?)ToNullableDbTime(session.EndedAt) ?? DBNull.Value);
    }

    private async Task FinishFocusSessionAsync(int sessionId, int actualSeconds, int distractionCount, string status)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE focus_sessions
            SET actual_seconds = $actual_seconds,
                distraction_count = $distraction_count,
                status = $status,
                ended_at = $ended_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$actual_seconds", actualSeconds);
        command.Parameters.AddWithValue("$distraction_count", distractionCount);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$ended_at", ToDbTime(DateTime.Now));
        command.ExecuteNonQuery();
        await Task.CompletedTask;
    }

    private async Task<int> ScalarIntAsync(string sql, DateTime startTime, DateTime endTime)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$start", ToDbTime(startTime));
        command.Parameters.AddWithValue("$end", ToDbTime(endTime));
        var value = command.ExecuteScalar();
        await Task.CompletedTask;
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static FocusSession ReadFocusSession(SqliteDataReader reader)
    {
        return new FocusSession
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            TargetType = reader.GetString(2),
            TargetValue = reader.GetString(3),
            PlannedSeconds = reader.GetInt32(4),
            ActualSeconds = reader.GetInt32(5),
            DistractionCount = reader.GetInt32(6),
            Status = reader.GetString(7),
            StartedAt = FromDbTime(reader.GetString(8)),
            EndedAt = reader.IsDBNull(9) ? null : FromDbTime(reader.GetString(9))
        };
    }

    private static void AddRangeParameters(SqliteCommand command)
    {
        var today = DateTime.Today;
        command.Parameters.AddWithValue("$today_start", ToDbTime(today));
        command.Parameters.AddWithValue("$last7_start", ToDbTime(today.AddDays(-6)));
        command.Parameters.AddWithValue("$last30_start", ToDbTime(today.AddDays(-29)));
        command.Parameters.AddWithValue("$tomorrow_start", ToDbTime(today.AddDays(1)));
        command.Parameters.AddWithValue("$self_process", SelfProcessName);
    }

    private static void AddRangeQueryParameters(SqliteCommand command, DateTime startTime, DateTime endTime)
    {
        command.Parameters.AddWithValue("$start", ToDbTime(startTime));
        command.Parameters.AddWithValue("$end", ToDbTime(endTime));
        command.Parameters.AddWithValue("$self_process", SelfProcessName);
    }

    private static void ApplySharePercent(List<UsageShareItem> items)
    {
        var total = Math.Max(1, items.Sum(item => item.DurationSeconds));
        foreach (var item in items)
        {
            item.Percentage = item.DurationSeconds * 100.0 / total;
        }
    }

    private static void ApplyDailyPercent(List<DailyUsageStat> items)
    {
        var max = Math.Max(1, items.Count == 0 ? 0 : items.Max(item => item.DurationSeconds));
        foreach (var item in items)
        {
            item.Percent = item.DurationSeconds * 100.0 / max;
        }
    }

    private static string BuildRangeWhere(SqliteCommand command, DateTime? startTime, DateTime? endTime, bool excludeIgnored)
    {
        command.Parameters.AddWithValue("$self_process", SelfProcessName);
        var clauses = new List<string>
        {
            "a.process_name <> $self_process"
        };

        if (excludeIgnored)
        {
            clauses.Add("COALESCE(a.is_ignored, 0) = 0");
        }

        if (startTime is not null)
        {
            clauses.Add("s.start_time >= $start");
            command.Parameters.AddWithValue("$start", ToDbTime(startTime.Value));
        }

        if (endTime is not null)
        {
            clauses.Add("s.start_time < $end");
            command.Parameters.AddWithValue("$end", ToDbTime(endTime.Value));
        }

        return "WHERE " + string.Join(" AND ", clauses);
    }

    private static string ToDbTime(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string? ToNullableDbTime(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static DateTime FromDbTime(string value)
    {
        return DateTime.Parse(value);
    }
}
