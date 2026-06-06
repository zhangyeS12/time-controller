using System.Windows;
using AppTimeTracker.Models;
using AppTimeTracker.Services;
using AppTimeTracker.ViewModels;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AppTimeTracker.Views;

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon? notifyIcon;
    private WinForms.ToolStripMenuItem? todayTotalMenuItem;
    private bool isRealExit;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.RequestHideToTray += (_, _) => HideToTray();
            viewModel.RequestNotification += (_, notification) => ShowNotification(notification);
        }

        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel
                && viewModel.StartMinimizedToTray
                && WasStartedMinimizedToTray())
            {
                HideToTray();
            }
        };
    }

    private void DashboardNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DashboardPage);
        SetActiveNav(DashboardNavButton);
    }

    private async void HistoryNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(HistoryPage);
        SetActiveNav(HistoryNavButton);
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadHistoryPageAsync();
        }
    }

    private async void AppsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AppManagementPage);
        SetActiveNav(AppsNavButton);
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadAppManagementPageAsync();
        }
    }

    private async void RulesNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(FocusRulesPage);
        SetActiveNav(RulesNavButton);
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadFocusRulesPageAsync();
        }
    }

    private async void FocusSessionNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(FocusSessionPage);
        SetActiveNav(FocusSessionNavButton);
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadFocusSessionPageAsync();
        }
    }

    private async void ReportsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(ReportsPage);
        SetActiveNav(ReportsNavButton);
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadReportsPageAsync();
        }
    }

    private async void DataNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DataSettingsPage);
        SetActiveNav(DataNavButton);
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadDataSettingsPageAsync();
        }
    }

    private void ShowPage(UIElement visiblePage)
    {
        DashboardPage.Visibility = visiblePage == DashboardPage ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = visiblePage == HistoryPage ? Visibility.Visible : Visibility.Collapsed;
        AppManagementPage.Visibility = visiblePage == AppManagementPage ? Visibility.Visible : Visibility.Collapsed;
        FocusRulesPage.Visibility = visiblePage == FocusRulesPage ? Visibility.Visible : Visibility.Collapsed;
        FocusSessionPage.Visibility = visiblePage == FocusSessionPage ? Visibility.Visible : Visibility.Collapsed;
        ReportsPage.Visibility = visiblePage == ReportsPage ? Visibility.Visible : Visibility.Collapsed;
        DataSettingsPage.Visibility = visiblePage == DataSettingsPage ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = visiblePage == PlaceholderPage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetActiveNav(System.Windows.Controls.Button activeButton)
    {
        ResetNavButton(DashboardNavButton);
        ResetNavButton(HistoryNavButton);
        ResetNavButton(AppsNavButton);
        ResetNavButton(RulesNavButton);
        ResetNavButton(FocusSessionNavButton);
        ResetNavButton(ReportsNavButton);
        ResetNavButton(DataNavButton);
        activeButton.Style = (Style)FindResource("SidebarButtonActive");
    }

    private void ResetNavButton(System.Windows.Controls.Button button)
    {
        button.Style = (Style)FindResource("SidebarButton");
    }

    private void SetupTrayIcon()
    {
        try
        {
            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Opening += (_, _) => UpdateTrayMenuText();

            var showMenuItem = new WinForms.ToolStripMenuItem("显示主窗口");
            showMenuItem.Click += (_, _) => ShowMainWindow();

            var pauseMenuItem = new WinForms.ToolStripMenuItem("暂停统计");
            pauseMenuItem.Click += (_, _) =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.PauseTracking();
                }
            };

            var resumeMenuItem = new WinForms.ToolStripMenuItem("继续统计");
            resumeMenuItem.Click += (_, _) =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.ResumeTracking();
                }
            };

            todayTotalMenuItem = new WinForms.ToolStripMenuItem("今日总时长：0秒")
            {
                Enabled = false
            };

            var exitMenuItem = new WinForms.ToolStripMenuItem("退出程序");
            exitMenuItem.Click += async (_, _) => await ExitApplicationAsync();

            contextMenu.Items.Add(showMenuItem);
            contextMenu.Items.Add(pauseMenuItem);
            contextMenu.Items.Add(resumeMenuItem);
            contextMenu.Items.Add(todayTotalMenuItem);
            contextMenu.Items.Add(new WinForms.ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            notifyIcon = new WinForms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "time-controller",
                Visible = true,
                ContextMenuStrip = contextMenu
            };
            notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
            LogService.Info("Tray icon initialized");
        }
        catch (Exception ex)
        {
            LogService.Exception("Tray initialization failed", ex);
            notifyIcon = null;
        }
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/time-controller.ico"));
        return resource is null
            ? Drawing.SystemIcons.Application
            : new Drawing.Icon(resource.Stream);
    }

    private void ShowTrayNotification(string message)
    {
        if (notifyIcon is null)
        {
            return;
        }

        try
        {
            notifyIcon.BalloonTipTitle = "time-controller 提醒";
            notifyIcon.BalloonTipText = message.Replace("time-controller 提醒：", "");
            notifyIcon.ShowBalloonTip(5000);
        }
        catch (Exception ex)
        {
            LogService.Exception("Tray notification failed", ex);
        }
    }

    private async void ShowNotification(NotificationMessage notification)
    {
        ShowTrayNotification(notification.Title + "：" + notification.Message);

        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        ToastTitle.Text = notification.Title;
        ToastMessage.Text = notification.Message;
        ToastHost.Visibility = Visibility.Visible;
        await Task.Delay(3000);
        ToastHost.Visibility = Visibility.Collapsed;
    }

    private static bool WasStartedMinimizedToTray()
    {
        return Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--minimized-to-tray", StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateTrayMenuText()
    {
        if (todayTotalMenuItem is null)
        {
            return;
        }

        var totalTime = DataContext is MainViewModel viewModel ? viewModel.TodayTotalTime : "0秒";
        todayTotalMenuItem.Text = "今日总时长：" + totalTime;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private async Task ExitApplicationAsync()
    {
        isRealExit = true;

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.CreateExitBackupIfEnabledAsync();
            viewModel.StopTracking();
        }

        LogService.Info("App exit requested from tray");
        DisposeTrayIcon();
        System.Windows.Application.Current.Shutdown();
    }

    private void DisposeTrayIcon()
    {
        if (notifyIcon is null)
        {
            return;
        }

        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        notifyIcon = null;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!isRealExit && DataContext is MainViewModel closingViewModel && closingViewModel.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CreateExitBackupIfEnabledAsync().GetAwaiter().GetResult();
            viewModel.StopTracking();
        }

        DisposeTrayIcon();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeTrayIcon();
        base.OnClosed(e);
    }
}
