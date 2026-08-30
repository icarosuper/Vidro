using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class ApiSettings
{
    [Required]
    public string BaseUrl { get; set; } = null!;
}
