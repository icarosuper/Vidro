using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class JwtSettings
{
    [Required]
    public string Secret { get; set; } = null!;

    [Required, Range(1, int.MaxValue)]
    public int AccessTokenExpiryMinutes { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int RefreshTokenExpiryDays { get; set; }
}
