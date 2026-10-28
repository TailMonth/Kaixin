using System.Runtime.InteropServices;

namespace KaixinPet.App;

internal static class CursorUtil
{
    public static void GetPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return;
        }

        x = 0;
        y = 0;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
