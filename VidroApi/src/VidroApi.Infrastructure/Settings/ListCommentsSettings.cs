using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class ListCommentsSettings
{
    [Range(1, int.MaxValue)]
    public int MaxLimit { get; init; }

    [Range(1, int.MaxValue)]
    public int MaxPopularLimit { get; init; }
}
