using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class WebhookSettings
{
    [Required]
    public string Secret { get; set; } = null!;

    [Required]
    public string MinioUploadToken { get; set; } = null!;
}
