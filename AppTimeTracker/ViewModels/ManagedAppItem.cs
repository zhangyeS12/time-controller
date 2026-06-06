using System.ComponentModel;
using System.Runtime.CompilerServices;
using AppTimeTracker.Models;

namespace AppTimeTracker.ViewModels;

public sealed class ManagedAppItem : INotifyPropertyChanged
{
    private string displayName;
    private string category;
    private bool isIgnored;

    public ManagedAppItem(ManagedAppInfo app)
    {
        Id = app.Id;
        ProcessName = app.ProcessName;
        displayName = app.DisplayName;
        category = app.Category;
        isIgnored = app.IsIgnored;
        TodayDurationSeconds = app.TodayDurationSeconds;
        Last7DaysDurationSeconds = app.Last7DaysDurationSeconds;
        Last30DaysDurationSeconds = app.Last30DaysDurationSeconds;
        TotalDurationSeconds = app.TotalDurationSeconds;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }
    public string ProcessName { get; }
    public int TodayDurationSeconds { get; private set; }
    public int Last7DaysDurationSeconds { get; private set; }
    public int Last30DaysDurationSeconds { get; private set; }
    public int TotalDurationSeconds { get; private set; }
    public string TodayDurationText => TimeFormatter.Format(TodayDurationSeconds);
    public string Last7DaysDurationText => TimeFormatter.Format(Last7DaysDurationSeconds);
    public string Last30DaysDurationText => TimeFormatter.Format(Last30DaysDurationSeconds);
    public string TotalDurationText => TimeFormatter.Format(TotalDurationSeconds);

    public string DisplayName
    {
        get => displayName;
        set => SetField(ref displayName, value);
    }

    public string Category
    {
        get => category;
        set => SetField(ref category, value);
    }

    public bool IsIgnored
    {
        get => isIgnored;
        set => SetField(ref isIgnored, value);
    }

    public void UpdateStats(int todayDurationSeconds, int last7DaysDurationSeconds, int last30DaysDurationSeconds, int totalDurationSeconds)
    {
        TodayDurationSeconds = todayDurationSeconds;
        Last7DaysDurationSeconds = last7DaysDurationSeconds;
        Last30DaysDurationSeconds = last30DaysDurationSeconds;
        TotalDurationSeconds = totalDurationSeconds;
        OnPropertyChanged(nameof(TodayDurationSeconds));
        OnPropertyChanged(nameof(Last7DaysDurationSeconds));
        OnPropertyChanged(nameof(Last30DaysDurationSeconds));
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(TodayDurationText));
        OnPropertyChanged(nameof(Last7DaysDurationText));
        OnPropertyChanged(nameof(Last30DaysDurationText));
        OnPropertyChanged(nameof(TotalDurationText));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
