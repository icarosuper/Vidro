using CSharpFunctionalExtensions;
using VidroApi.Api.Common;
using VidroApi.Domain.Errors;
using IHttpResult = Microsoft.AspNetCore.Http.IResult;

namespace VidroApi.Api.Extensions;

public static class ResultExtensions
{
    public static IHttpResult ToApiResult<T>(this Result<T, Error> result, int successStatusCode)
    {
        if (result.IsFailure)
            return ApiResponse.Fail(result.Error);

        return successStatusCode switch
        {
            StatusCodes.Status204NoContent => ApiResponse.NoContent(),
            _ => Results.Json(new ApiResponse<T>(result.Value), statusCode: successStatusCode)
        };
    }

    public static IHttpResult ToApiResult(this UnitResult<Error> result)
    {
        return result.IsFailure
            ? ApiResponse.Fail(result.Error)
            : ApiResponse.NoContent();
    }
}
