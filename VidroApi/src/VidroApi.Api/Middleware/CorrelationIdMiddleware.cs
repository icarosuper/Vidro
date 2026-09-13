using VidroApi.Api.Common.Logging;

namespace VidroApi.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = LoggingDefaults.CorrelationIdHeader;

    /// <summary>
    /// The correlation ID of the current request, as this middleware resolved it — inbound header
    /// or freshly generated. Read from the response headers because that is where the middleware
    /// always writes it, even when the request brought none.
    /// </summary>
    public static string GetCorrelationId(HttpContext ctx) =>
        ctx.Response.Headers[CorrelationIdHeader].ToString();

    public async Task InvokeAsync(HttpContext ctx)
    {
        var correlationId = ctx.Request.Headers.TryGetValue(CorrelationIdHeader, out var inbound)
            ? inbound.ToString()
            : Guid.NewGuid().ToString();

        ctx.Response.Headers[CorrelationIdHeader] = correlationId;

        var methodName = $"{ctx.Request.Method} {ctx.Request.Path}";

        using var scope = ProcessingLogScope.Begin(
            processType: LoggingDefaults.StepProcessType,
            correlationId: correlationId,
            methodName: methodName);

        await next(ctx);
    }
}
