using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using VidroApi.Api.Common;
using VidroApi.Domain.Enums;

namespace VidroApi.Api.Extensions;

/// <summary>
/// Shapes <c>openapi/v1.json</c> so the front can generate its types from it: one schema per CLR
/// type (the vertical slices all name their records <c>Request</c>/<c>Response</c>) and domain
/// enums with their numeric values, plus the domain error body. See design-decisions.md #15.
/// </summary>
public static class OpenApiExtensions
{
    private static readonly string DomainEnumsNamespace = typeof(VideoStatus).Namespace!;

    public static IServiceCollection AddVidroOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.CreateSchemaReferenceId = jsonTypeInfo =>
            {
                var defaultId = OpenApiOptions.CreateDefaultSchemaReferenceId(jsonTypeInfo);
                var isInlinedByDefault = defaultId is null;
                return isInlinedByDefault ? null : SchemaIdFor(jsonTypeInfo.Type);
            };
            options.AddSchemaTransformer((schema, context, _) =>
            {
                AddDomainEnumValues(schema, context.JsonTypeInfo.Type);
                DropNumberAsStringAlternative(schema);
                RequireEveryProperty(schema);
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer(DeclareDomainErrorBodies);
        });

    /// <summary>
    /// Every <c>/v1</c> 4xx that carries a body is an <see cref="ApiErrorResponse"/> — domain errors
    /// through <c>ToApiResult</c> and malformed requests through <c>ExceptionMiddleware</c>. One
    /// <c>4XX</c> range on each operation says that once, instead of 43 <c>.Produces</c> guessing
    /// which statuses each handler can reach. The exceptions are in the description; the
    /// FluentValidation body is the open follow-up in docs/TODO.md.
    /// </summary>
    private static async Task DeclareDomainErrorBodies(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken ct)
    {
        var errorSchemaId = SchemaIdFor(typeof(ApiErrorResponse));
        var errorSchema = await context.GetOrCreateSchemaAsync(typeof(ApiErrorResponse), cancellationToken: ct);
        document.AddComponent(errorSchemaId, errorSchema);

        var v1Operations = document.Paths
            .Where(path => path.Key.StartsWith("/v1/"))
            .SelectMany(path => path.Value.Operations?.Values.AsEnumerable() ?? []);

        foreach (var operation in v1Operations)
        {
            operation.Responses ??= new OpenApiResponses();
            operation.Responses["4XX"] = new OpenApiResponse
            {
                Description = DomainErrorDescription,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new() { Schema = new OpenApiSchemaReference(errorSchemaId, document) }
                }
            };
        }
    }

    private const string DomainErrorDescription =
        "Domain or request error: { code, message }. Exceptions with another body: a FluentValidation " +
        "400 is { errors: [{ field, message }] }; a 401 for a missing or invalid token and a 429 from " +
        "the rate limiter have no body.";

    /// <summary>
    /// <c>CreateVideo.Request</c> → <c>CreateVideoRequest</c>,
    /// <c>ApiResponse&lt;GetVideo.Response&gt;</c> → <c>GetVideoResponseApiResponse</c>.
    /// ASP.NET's default is the bare type name, which collapses every slice's <c>Request</c> into one.
    /// </summary>
    public static string SchemaIdFor(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsGenericType)
        {
            var genericDefinition = underlyingType.GetGenericTypeDefinition();
            var argumentIds = string.Concat(underlyingType.GetGenericArguments().Select(SchemaIdFor));
            return argumentIds + NestedTypeName(genericDefinition);
        }

        return NestedTypeName(underlyingType);
    }

    private static string NestedTypeName(Type type)
    {
        var nameWithoutArity = type.Name.Split('`')[0];
        return type.DeclaringType is null
            ? nameWithoutArity
            : NestedTypeName(type.DeclaringType) + nameWithoutArity;
    }

    private static void AddDomainEnumValues(OpenApiSchema schema, Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        var isDomainEnum = underlyingType.IsEnum && underlyingType.Namespace == DomainEnumsNamespace;
        if (!isDomainEnum)
            return;

        schema.Type = JsonSchemaType.Integer;
        schema.Enum = Enum.GetValues(underlyingType)
            .Cast<object>()
            .Select(value => (JsonNode)JsonValue.Create(Convert.ToInt32(value)))
            .ToList();
    }

    /// <summary>
    /// The web JSON defaults read <c>"5"</c> as a number, so ASP.NET describes every number as
    /// <c>integer | string</c> plus a digits pattern. The API always writes numbers, and the front
    /// always sends them; the string alternative would only turn every count into
    /// <c>number | string</c> in the generated types.
    /// </summary>
    private static void DropNumberAsStringAlternative(OpenApiSchema schema)
    {
        const JsonSchemaType numericTypes = JsonSchemaType.Integer | JsonSchemaType.Number;
        var isNumberAcceptingStrings = schema.Type is { } type
            && (type & numericTypes) != 0
            && type.HasFlag(JsonSchemaType.String);
        if (!isNumberAcceptingStrings)
            return;

        schema.Type &= ~JsonSchemaType.String;
        schema.Pattern = null;
    }

    /// <summary>
    /// The records use <c>init</c> properties, which ASP.NET never marks required, so every field
    /// would be optional in the generated types. System.Text.Json writes every property, null
    /// included, so each one is always present; nullability stays in the property's own type.
    /// </summary>
    private static void RequireEveryProperty(OpenApiSchema schema)
    {
        var hasProperties = schema.Properties is { Count: > 0 };
        if (!hasProperties)
            return;

        schema.Required = new HashSet<string>(schema.Properties!.Keys);
    }
}
