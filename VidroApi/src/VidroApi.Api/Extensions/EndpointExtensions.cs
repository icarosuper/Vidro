using System.Reflection;

namespace VidroApi.Api.Extensions;

public static class EndpointExtensions
{
    /// <summary>
    /// Scans the calling assembly for all feature classes with a public static MapEndpoint method
    /// and registers them automatically — no need to call MapEndpoint manually per feature.
    /// </summary>
    public static void MapAllEndpoints(this WebApplication app)
    {
        var endpointTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass)
            .Select(t => (Type: t, Method: t.GetMethod("MapEndpoint", BindingFlags.Public | BindingFlags.Static)))
            .Where(x => x.Method is not null);

        foreach (var (_, method) in endpointTypes)
            method!.Invoke(null, [app]);
    }
}
