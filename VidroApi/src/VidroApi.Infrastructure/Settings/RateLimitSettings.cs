using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class RateLimitSettings
{
    /// <summary>Name of the policy guarding the credential endpoints (sign in / sign up).</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Requests allowed per client IP, per <see cref="AuthWindowSeconds"/>.</summary>
    [Required, Range(1, int.MaxValue)]
    public int AuthPermitLimit { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int AuthWindowSeconds { get; set; }
}
