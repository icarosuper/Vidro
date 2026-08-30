using VidroApi.Api.Common.Logging;

namespace VidroApi.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-ID";

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
