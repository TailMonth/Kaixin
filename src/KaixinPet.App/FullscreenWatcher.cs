using System.Runtime.InteropServices;
using System.Text;

namespace KaixinPet.App;

internal static class FullscreenWatcher
{
    private const int QunsRunningD3DFullScreen = 3;
    private const int QunsPresentationMode = 4;

    public static bool IsFullscreen(params IntPtr[] excludedWindows)
    {
        if (SHQueryUserNotificationState(out var state) == 0 &&
            state is QunsRunningD3DFullScreen or QunsPresentationMode)
        {
            return true;
        }

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        foreground = GetAncestor(foreground, 2); // GA_ROOT
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground) || IsIconic(foreground))
        {
            return false;
        }

        foreach (var excluded in excludedWindows)
        {
            if (excluded != IntPtr.Zero && (excluded == foreground || IsChild(excluded, foreground)))
            {
                return false;
            }
        }

        var className = new StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        var name = className.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, 2); // MONITOR_DEFAULTTONEAREST
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        const int tolerance = 2;
        return Math.Abs(rect.Left - info.rcMonitor.Left) <= tolerance &&
               Math.Abs(rect.Top - info.rcMonitor.Top) <= tolerance &&
               Math.Abs(rect.Right - info.rcMonitor.Right) <= tolerance &&
               Math.Abs(rect.Bottom - info.rcMonitor.Bottom) <= tolerance;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
