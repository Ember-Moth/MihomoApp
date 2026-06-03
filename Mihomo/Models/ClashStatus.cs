namespace Mihomo.Models;

public enum ClashRunState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error
}

public sealed record ClashStatus(
    ClashRunState State,
    string Message,
    long StartedAtUnixMilliseconds = 0)
{
    public static ClashStatus Stopped { get; } = new(ClashRunState.Stopped, "Stopped");
}
