using System.Text.Json;
using FluentValidation;

namespace VidroApi.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ValidationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "application/json";

            var errors = ex.Errors
                .Select(e => new { field = e.PropertyName, message = e.ErrorMessage });

            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { errors }));
        }
        catch (BadHttpRequestException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { code = "bad_request", message = ex.Message }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { code = "internal_error", message = "An unexpected error occurred." }));
        }
    }
}
