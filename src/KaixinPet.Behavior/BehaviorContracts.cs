namespace KaixinPet.Behavior;

public enum PetBehaviorState
{
    Idle,
    Sleep,
    Reading,
    Drag,
    Fall
}

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

    public bool EffectsEnabled { get; init; } = true;
}
