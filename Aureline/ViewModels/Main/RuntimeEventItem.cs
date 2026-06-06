namespace Aureline.ViewModels;

public sealed record RuntimeEventItem(DateTimeOffset Time, string Message)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss");
}
