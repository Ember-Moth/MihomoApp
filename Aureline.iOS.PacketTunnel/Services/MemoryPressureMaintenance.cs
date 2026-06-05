using System.Threading;

namespace Aureline.iOS.PacketTunnel.Services;

internal sealed class MemoryPressureMaintenance : IDisposable
{
    private static readonly TimeSpan StartupInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SteadyInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupWindow = TimeSpan.FromMinutes(2);

    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private readonly Timer timer;
    private int disposed;

    public MemoryPressureMaintenance()
    {
        timer = new Timer(static state => ((MemoryPressureMaintenance)state!).Tick(), this, StartupInterval, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        timer.Dispose();
    }

    private void Tick()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        MemoryPressure.Trim();
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var interval = DateTimeOffset.UtcNow - startedAt < StartupWindow
            ? StartupInterval
            : SteadyInterval;

        timer.Change(interval, Timeout.InfiniteTimeSpan);
    }
}
