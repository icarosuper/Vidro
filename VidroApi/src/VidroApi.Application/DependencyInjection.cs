using System.Reflection;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using VidroApi.Application.Behaviors;
using VidroApi.Application.Common;

namespace VidroApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, params Assembly[] featureAssemblies)
    {
        var assemblies = new[] { typeof(DependencyInjection).Assembly }.Concat(featureAssemblies).ToArray();

        services.AddValidatorsFromAssemblies(assemblies);

        // Logging runs outermost so it captures validation errors and handler exceptions.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestLoggingPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
