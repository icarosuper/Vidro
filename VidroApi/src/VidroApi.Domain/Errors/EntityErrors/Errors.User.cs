namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class User
    {
        public static Error EmailAlreadyInUse() =>
            new("user.email_conflict", "The email address is already in use.", ErrorType.Conflict);

        public static Error UsernameAlreadyTaken() =>
            new("user.username_conflict", "The username is already taken.", ErrorType.Conflict);

        public static Error IncorrectPassword() =>
            new("user.incorrect_password", "The informed password is incorrect.", ErrorType.Unauthorized);

        public static Error InvalidCredentials() =>
            new("user.invalid_credentials", "The provided credentials are invalid.", ErrorType.Unauthorized);
    }
}
