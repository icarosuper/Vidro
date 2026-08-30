using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class ListRepliesSettings
{
    [Range(1, int.MaxValue)]
    public int MaxLimit { get; init; }
}
