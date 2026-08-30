namespace VidroApi.Domain.Errors;

public static class CommonErrors
{
    public static Error NotFound(string resource, Guid id) =>
        new("resource.not_found", $"'{resource}' with id '{id}' was not found.", ErrorType.NotFound);

    public static Error NotFound(string resource, string identifier) =>
        new("resource.not_found", $"'{resource}' '{identifier}' was not found.", ErrorType.NotFound);

    public static Error Unauthorized(string message = "Authentication is required.") =>
        new("request.unauthorized", message, ErrorType.Unauthorized);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        new("request.forbidden", message, ErrorType.Forbidden);

    public static Error InternalServerError(string message = "An unexpected error occurred.") =>
        new("internal.server_error", message, ErrorType.Validation);
}
