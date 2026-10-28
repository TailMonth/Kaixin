using KaixinPet.Pet.Native;
using System.Runtime.InteropServices;

namespace KaixinPet.Pet.Native;

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

public static class ScreenUtil
{
    public static PixelRect GetPrimaryWorkArea()
    {
        var rect = default(NativeMethods.RECT);
        if (NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref rect, 0))
        {
            return new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        return new PixelRect(0, 0, 1920, 1080);
    }

    public static PixelRect GetWorkAreaForRect(PixelRect rect)
    {
        var native = new NativeMethods.RECT(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var monitor = NativeMethods.MonitorFromRect(ref native, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height);
        }

        return GetPrimaryWorkArea();
    }

    public static PixelRect GetWorkAreaForWindow(IntPtr hwnd)
    {
        if (NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return GetWorkAreaForRect(new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height));
        }

        return GetPrimaryWorkArea();
    }
}
