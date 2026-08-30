using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class ListTrendingVideosSettings
{
    [Range(1, int.MaxValue)]
    public int MaxLimit { get; init; }
}
