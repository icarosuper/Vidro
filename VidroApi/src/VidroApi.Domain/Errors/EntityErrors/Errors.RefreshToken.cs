namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class RefreshToken
    {
        public static Error NotFound() =>
            new("refresh_token.not_found", "Refresh token not found.", ErrorType.Unauthorized);

        public static Error Expired() =>
            new("refresh_token.expired", "The refresh token has expired.", ErrorType.Unauthorized);

        public static Error Revoked() =>
            new("refresh_token.revoked", "The refresh token has been revoked.", ErrorType.Unauthorized);
    }
}
