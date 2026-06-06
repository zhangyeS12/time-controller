using System.Runtime.InteropServices;

namespace AppTimeTracker.Services;

public sealed class IdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    public TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var idleMilliseconds = Environment.TickCount64 - info.Time;
        return TimeSpan.FromMilliseconds(Math.Max(0, idleMilliseconds));
    }

    public bool IsIdle(TimeSpan threshold)
    {
        return GetIdleTime() >= threshold;
    }
}
