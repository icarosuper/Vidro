using VidroApi.Api.Common;

namespace VidroApi.Api.Extensions;

/// <summary>
/// Declares on the route what <see cref="ResultExtensions.ToApiResult{T}"/> returns on success, so
/// the success body reaches <c>openapi/v1.json</c> and, from there, the front's generated types.
/// Handlers stay on <c>IResult</c>; error statuses are not declared here.
/// </summary>
public static class RouteHandlerBuilderExtensions
{
    public static RouteHandlerBuilder ProducesApiSuccess<T>(
        this RouteHandlerBuilder builder,
        int statusCode = StatusCodes.Status200OK) =>
        builder.Produces<ApiResponse<T>>(statusCode, "application/json");

    public static RouteHandlerBuilder ProducesNoContent(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status204NoContent);
}
