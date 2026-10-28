using System.Diagnostics;

namespace KaixinPet.App;

public sealed class TimerDemo
{
    private readonly Stopwatch _watch = new();
    private TimeSpan _duration;
    private bool _completedRaised;

    public bool IsRunning { get; private set; } = false;

    public TimeSpan Duration => _duration;

    public TimeSpan Remaining
    {
        get
        {
            if (_duration <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var remaining = _duration - _watch.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public double Progress
    {
        get
        {
            if (_duration <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Clamp(_watch.Elapsed / _duration, 0, 1);
        }
    }

    public void Start(TimeSpan duration)
    {
        _duration = duration;
        _completedRaised = false;
        _watch.Restart();
        IsRunning = true;
    }

    public void Stop()
    {
        _watch.Stop();
        IsRunning = false;
    }

    public bool TickAndCheckCompleted()
    {
        if (!IsRunning || _completedRaised)
        {
            return false;
        }

        if (Remaining > TimeSpan.Zero)
        {
            return false;
        }

        _completedRaised = true;
        return true;
    }
}
