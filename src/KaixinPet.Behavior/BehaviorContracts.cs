namespace KaixinPet.Behavior;

public enum PetBehaviorState
{
    Idle,
    Sleep,
    Play,
    Drag,
    Fall
}

public readonly record struct BallState(double X, double Y, int Frame);

public interface IPetAnimationSink
{
    string? CurrentBaseClip { get; }

    void PlayBase(string clip, string? next = null);

    void PlayOverlay(string clip);

    void SetLoopOverlay(string clip, bool enabled);
}

public sealed class BehaviorOptions
{
    public int SleepAfterIdleMinutes { get; init; } = 15;

    public int PlaySessionMaxSeconds { get; init; } = 90;

    public bool SpontaneousPlay { get; init; } = true;

    public double GroundYDip { get; init; } = 264;

    public double WindowWidthDip { get; init; } = 320;

    public double CatCenterXDip { get; init; } = 110;
}
