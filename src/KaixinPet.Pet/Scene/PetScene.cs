namespace KaixinPet.Pet.Scene;

public sealed class PetScene
{
    public bool TimerVisible { get; private set; }

    public bool IsDragging { get; private set; }

    public double Progress { get; private set; }

    public double CatOffsetXDip { get; private set; }

    public bool Sleeping { get; private set; }

    public bool BallVisible { get; private set; }

    public double BallXDip { get; private set; }

    public double BallYDip { get; private set; }

    public int BallFrame { get; private set; }

    public void SetTimerVisible(bool visible) => TimerVisible = visible;

    public void SetProgress(double progress) => Progress = Math.Clamp(progress, 0, 1);

    public void SetDragging(bool dragging) => IsDragging = dragging;

    public void SetCatOffset(double offsetDip) => CatOffsetXDip = offsetDip;

    public void SetSleeping(bool sleeping) => Sleeping = sleeping;

    public void SetBall(double xDip, double yDip, int frame)
    {
        BallVisible = true;
        BallXDip = xDip;
        BallYDip = yDip;
        BallFrame = frame;
    }

    public void ClearBall()
    {
        BallVisible = false;
        BallFrame = 0;
    }

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
