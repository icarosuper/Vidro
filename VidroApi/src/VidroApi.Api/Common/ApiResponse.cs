using VidroApi.Domain.Errors;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace VidroApi.Api.Common;

public sealed record ApiResponse<T>(T Data);

public sealed record ApiErrorResponse(string Code, string Message);

public static class ApiResponse
{
    public static IHttpResult Ok<T>(T data) =>
        Results.Ok(new ApiResponse<T>(data));

    public static IHttpResult Created<T>(T data, string location) =>
        Results.Created(location, new ApiResponse<T>(data));

    public static IHttpResult NoContent() =>
        Results.NoContent();

    public static IHttpResult Fail(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(new ApiErrorResponse(error.Code, error.Message), statusCode: statusCode);
    }
}

