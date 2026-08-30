using System.Security.Claims;

namespace VidroApi.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("User ID claim is missing or invalid.");
    }
}
