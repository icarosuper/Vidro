using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class SearchSettings
{
    [Required, Range(1, int.MaxValue)]
    public int MaxLimit { get; set; }
}
