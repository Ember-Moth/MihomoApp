using CommunityToolkit.Mvvm.ComponentModel;
using Aureline.Services.Clash;

namespace Aureline.ViewModels;

public sealed partial class InstalledApplicationItem : ObservableObject
{
    public InstalledApplicationItem(ClashInstalledApplication application)
    {
        PackageName = application.PackageName;
        Label = string.IsNullOrWhiteSpace(application.Label)
            ? application.PackageName
            : application.Label;
        IsSystem = application.IsSystem;
        UsesInternet = application.UsesInternet;
        LastUpdateUnixMilliseconds = application.LastUpdateUnixMilliseconds;
    }

    public string PackageName { get; }

    public string Label { get; }

    public bool IsSystem { get; }

    public bool UsesInternet { get; }

    public long LastUpdateUnixMilliseconds { get; }

    public string Summary
    {
        get
        {
            var type = IsSystem ? "系统应用" : "用户应用";
            var network = UsesInternet ? "可联网" : "无联网权限";
            return $"{type} · {network} · {PackageName}";
        }
    }

    [ObservableProperty]
    private bool _isSelected;
}
