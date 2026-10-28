using System.Diagnostics;

namespace KaixinPet.Core.Diagnostics;

public sealed class ProcessMetrics
{
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    public ProcessMetrics()
    {
        _process.Refresh();
        _lastCpu = _process.TotalProcessorTime;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public long PrivateBytes
    {
        get
        {
            _process.Refresh();
            return _process.PrivateMemorySize64;
        }
    }

    public int HandleCount
    {
        get
        {
            _process.Refresh();
            return _process.HandleCount;
        }
    }

    public double SampleCpuPercent()
    {
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        var now = Stopwatch.GetTimestamp();

        var elapsedMs = (now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency;
        var cpuMs = (cpu - _lastCpu).TotalMilliseconds;

        _lastCpu = cpu;
        _lastTimestamp = now;

        if (elapsedMs <= 0)
        {
            return 0;
        }

        return cpuMs / (elapsedMs * Environment.ProcessorCount) * 100.0;
    }

    public static double ElapsedMs(long startTimestamp, long endTimestamp) =>
        (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
}
