namespace KaixinPet.Pet.Scene;

public sealed class PetScene
{
    public bool TimerVisible { get; private set; }

    public bool IsDragging { get; private set; }

    public double Progress { get; private set; }

    public void SetTimerVisible(bool visible) => TimerVisible = visible;

    public void SetProgress(double progress) => Progress = Math.Clamp(progress, 0, 1);

    public void SetDragging(bool dragging) => IsDragging = dragging;

    public bool IsPointInHourglass(int px, int py, double dpiScale)
    {
        if (!TimerVisible)
        {
            return false;
        }

        var left = (int)Math.Floor(SceneLayout.HourglassLeftDip * dpiScale);
        var top = (int)Math.Floor(SceneLayout.HourglassTopDip * dpiScale);
        var right = (int)Math.Ceiling((SceneLayout.HourglassLeftDip + SceneLayout.HourglassWidthDip) * dpiScale);
        var bottom = (int)Math.Ceiling((SceneLayout.HourglassTopDip + SceneLayout.HourglassHeightDip) * dpiScale);
        return px >= left && px < right && py >= top && py < bottom;
    }
}
