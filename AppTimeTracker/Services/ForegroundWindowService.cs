using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AppTimeTracker.Models;

namespace AppTimeTracker.Services;

public sealed class ForegroundWindowService
{
    private const int WindowTextLength = 512;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public TrackedWindowInfo? GetCurrentForegroundWindow(bool recordWindowTitle)
    {
        try
        {
            var windowHandle = GetForegroundWindow();
            if (windowHandle == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)processId);
            var processName = NormalizeProcessName(process.ProcessName);
            var displayName = GetDisplayName(process, processName);
            var title = recordWindowTitle ? GetTitle(windowHandle) : null;

            return new TrackedWindowInfo
            {
                ProcessName = processName,
                DisplayName = displayName,
                WindowTitle = title
            };
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeProcessName(string processName)
    {
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";
    }

    private static string GetDisplayName(Process process, string fallback)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }
        }
        catch
        {
            // Some protected processes do not allow module inspection.
        }

        return fallback;
    }

    private static string? GetTitle(IntPtr windowHandle)
    {
        var builder = new StringBuilder(WindowTextLength);
        var length = GetWindowText(windowHandle, builder, builder.Capacity);
        if (length <= 0)
        {
            return null;
        }

        return builder.ToString();
    }
}
