using CommunityToolkit.Mvvm.ComponentModel;

namespace Aureline.ViewModels;

public enum ConfigProfileType
{
    File,
    Url
}

public sealed partial class ConfigProfileItem : ObservableObject
{
    public ConfigProfileItem(
        int id,
        ConfigProfileType type,
        string label,
        string filePath,
        string url,
        DateTimeOffset? lastUpdateDate)
    {
        Id = id;
        Type = type;
        Label = string.IsNullOrWhiteSpace(label) ? "config.yaml" : label;
        FilePath = filePath;
        Url = url;
        LastUpdateDate = lastUpdateDate;
    }

    public int Id { get; }

    public ConfigProfileType Type { get; }

    public string Label { get; }

    public string FilePath { get; }

    public string Url { get; }

    public DateTimeOffset? LastUpdateDate { get; }

    [ObservableProperty]
    private bool _isSelected;

    public bool IsUrl => Type == ConfigProfileType.Url;

    public string TypeText => IsUrl ? "URL" : "File";

    public string SourceText => IsUrl ? "远程订阅" : "本地文件";

    public string UpdateText => string.IsNullOrWhiteSpace(LastUpdateText)
        ? "从未更新"
        : LastUpdateText;

    public string LastUpdateText
    {
        get
        {
            if (LastUpdateDate == null)
            {
                return string.Empty;
            }

            var elapsed = DateTimeOffset.Now - LastUpdateDate.Value;
            if (elapsed.TotalMinutes < 1)
            {
                return "刚刚更新";
            }

            if (elapsed.TotalHours < 1)
            {
                return $"{Math.Max(1, (int)elapsed.TotalMinutes)}分钟前更新";
            }

            if (elapsed.TotalDays < 1)
            {
                return $"{Math.Max(1, (int)elapsed.TotalHours)}小时前更新";
            }

            return $"{Math.Max(1, (int)elapsed.TotalDays)}天前更新";
        }
    }

    public string Description => Type == ConfigProfileType.Url
        ? Url
        : FilePath;
}

public sealed record PickedProfileFile(string Name, string Content);
