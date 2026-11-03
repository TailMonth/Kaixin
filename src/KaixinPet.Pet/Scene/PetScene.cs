namespace KaixinPet.Pet.Scene;

public sealed class PetScene
{
    public bool TimerVisible { get; private set; }

    public bool IsDragging { get; private set; }

    public double Progress { get; private set; }

    public bool SandBreak { get; private set; }

    public double HourglassAlpha { get; private set; } = 1;

    public double HourglassFlipScale { get; private set; } = 1;

    public bool Sleeping { get; private set; }

    public bool TimerRunning { get; private set; }

    public bool EffectsEnabled { get; private set; } = true;

    public double BookAlpha { get; private set; }

    public int BookFrame { get; private set; }

    public void SetTimerVisible(bool visible) => TimerVisible = visible;

    public void SetProgress(double progress) => Progress = Math.Clamp(progress, 0, 1);

    public void SetSandBreak(bool sandBreak) => SandBreak = sandBreak;

    public void SetTimerRunning(bool running) => TimerRunning = running;

    public void SetEffectsEnabled(bool enabled) => EffectsEnabled = enabled;

    public void SetHourglassVisual(double alpha, double flipScale)
    {
        HourglassAlpha = Math.Clamp(alpha, 0, 1);
        HourglassFlipScale = Math.Clamp(flipScale, 0.02, 1);
    }

    public void SetDragging(bool dragging) => IsDragging = dragging;

    public void SetSleeping(bool sleeping) => Sleeping = sleeping;

    public void SetBookVisual(double alpha, int frame)
    {
        BookAlpha = Math.Clamp(alpha, 0, 1);
        BookFrame = Math.Max(0, frame);
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
