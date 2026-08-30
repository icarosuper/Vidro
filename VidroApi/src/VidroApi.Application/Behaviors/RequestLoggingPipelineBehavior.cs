using System.Text.Json;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using VidroApi.Application.Common.Logging;
using VidroApi.Domain.Errors;

namespace VidroApi.Application.Behaviors;

public sealed class RequestLoggingPipelineBehavior<TMessage, TResponse>(
    ILogger<RequestLoggingPipelineBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        var featureName = typeof(TMessage).DeclaringType?.Name ?? typeof(TMessage).Name;
        var serializedRequest = JsonSerializer.Serialize(message, JsonLogSerializer.Options);

        logger.LogInformation("[{FeatureName}] Handling request.", featureName);

        try
        {
            var response = await next(message, ct);

            LogResponse(featureName, serializedRequest, response);

            return response;
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty(LoggingConstants.InputLogProperty, new JsonLogSerializer.RawJson(serializedRequest), destructureObjects: false))
                logger.LogError(ex, "[{FeatureName}] Request failed with unhandled exception.", featureName);

            throw;
        }
    }

    private void LogResponse(string featureName, string serializedRequest, TResponse response)
    {
        var isFailure = typeof(TResponse).GetProperty("IsFailure")?.GetValue(response) as bool?;

        if (isFailure is true)
        {
            var error = typeof(TResponse).GetProperty("Error")?.GetValue(response) as Error;

            using (LogContext.PushProperty(LoggingConstants.InputLogProperty, new JsonLogSerializer.RawJson(serializedRequest), destructureObjects: false))
            using (LogContext.PushProperty(LoggingConstants.ErrorLogProperty, error, destructureObjects: true))
                logger.LogWarning("[{FeatureName}] Request completed with domain error.", featureName);
        }
        else
        {
            var value = typeof(TResponse).GetProperty("Value")?.GetValue(response);
            var serializedResponse = JsonSerializer.Serialize(value, JsonLogSerializer.Options);

            using (LogContext.PushProperty(LoggingConstants.InputLogProperty, new JsonLogSerializer.RawJson(serializedRequest), destructureObjects: false))
            using (LogContext.PushProperty(LoggingConstants.OutputLogProperty, new JsonLogSerializer.RawJson(serializedResponse), destructureObjects: false))
                logger.LogInformation("[{FeatureName}] Request completed successfully.", featureName);
        }
    }
}
