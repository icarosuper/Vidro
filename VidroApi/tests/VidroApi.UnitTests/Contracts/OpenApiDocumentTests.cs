using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace VidroApi.UnitTests.Contracts;

/// <summary>
/// VidroFront generates its request/response types from the committed <c>openapi/v1.json</c>
/// (<c>VidroFront/src/shared/api/schema.d.ts</c>). That only locks anything if the document
/// describes the success bodies, gives each slice's <c>Request</c>/<c>Response</c> its own schema,
/// and lists the enum values. This reads the committed file — no host, no Docker — and fails if
/// a route loses its <c>.ProducesApiSuccess</c>, the schema ids collapse again, or an enum loses
/// its values. See VidroApi/openapi/README.md.
/// </summary>
public class OpenApiDocumentTests
{
    private static readonly JsonNode Document = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi", "v1.json")))!;

    private static readonly Dictionary<string, Dictionary<string, int>> EnumGolden =
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "enums.json")))!;

    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];

    private static JsonObject Schemas => Document["components"]!["schemas"]!.AsObject();

    private static IEnumerable<(string Path, string Method, JsonObject Responses)> Operations() =>
        from path in Document["paths"]!.AsObject()
        from method in HttpMethods
        let operation = path.Value![method]
        where operation is not null
        select (path.Key, method, operation["responses"]!.AsObject());

    [Fact]
    public void EveryV1Operation_ShouldDeclareItsSuccessBody()
    {
        var v1Operations = Operations().Where(op => op.Path.StartsWith("/v1/")).ToList();
        v1Operations.Should().NotBeEmpty();

        foreach (var (path, method, responses) in v1Operations)
        {
            var operationName = $"{method.ToUpperInvariant()} {path}";
            var successStatus = responses.Select(r => r.Key).SingleOrDefault(IsSuccessStatus);
            successStatus.Should().NotBeNull(
                $"{operationName} must declare its success status with .ProducesApiSuccess or .ProducesNoContent");

            var successBody = responses[successStatus!]!["content"]?["application/json"]?["schema"];
            var isNoContent = successStatus == "204";
            if (isNoContent)
                successBody.Should().BeNull($"{operationName} returns 204, which has no body");
            else
                successBody.Should().NotBeNull($"{operationName} returns {successStatus} with a body the front types");
        }
    }

    [Fact]
    public void EveryWebhook_ShouldDeclareAnEmptyOk()
    {
        var webhooks = Operations().Where(op => op.Path.StartsWith("/webhooks/")).ToList();
        webhooks.Should().NotBeEmpty();

        foreach (var (path, _, responses) in webhooks)
        {
            var declaresOk = responses.ContainsKey("200");
            declaresOk.Should().BeTrue($"{path} answers the caller with an empty 200");
        }
    }

    [Fact]
    public void EveryV1Operation_ShouldDeclareTheDomainErrorBody()
    {
        var v1Operations = Operations().Where(op => op.Path.StartsWith("/v1/")).ToList();
        v1Operations.Should().NotBeEmpty();

        foreach (var (path, method, responses) in v1Operations)
        {
            var errorBody = responses["4XX"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

            errorBody.Should().Be("#/components/schemas/ApiErrorResponse",
                $"{method.ToUpperInvariant()} {path} answers a domain error with {{ code, message }}, which the front types as ApiError");
        }
    }

    [Fact]
    public void DomainErrorBody_ShouldRequireCodeAndMessage()
    {
        var requiredFields = Schemas["ApiErrorResponse"]?["required"]?.AsArray().Select(field => field!.GetValue<string>());

        requiredFields.Should().BeEquivalentTo(["code", "message"],
            "the front reads error.code to react to API_ERROR_CODES and error.message to show the user");
    }

    [Fact]
    public void SchemaIds_ShouldNeverBeABareRequestOrResponse()
    {
        var bareSlotNames = Schemas.Select(schema => schema.Key)
            .Where(name => name is "Request" or "Response");

        bareSlotNames.Should().BeEmpty(
            "every slice names its records Request/Response, so a bare name means they collapsed into one schema");
    }

    [Fact]
    public void DomainEnums_ShouldListTheGoldenValues()
    {
        var enumsInDocument = EnumGolden.Keys.Where(Schemas.ContainsKey).ToList();
        enumsInDocument.Should().NotBeEmpty();

        foreach (var enumName in enumsInDocument)
        {
            var documentValues = Schemas[enumName]!["enum"]?.AsArray().Select(value => value!.GetValue<int>());

            documentValues.Should().BeEquivalentTo(EnumGolden[enumName].Values,
                $"{enumName} in openapi/v1.json must carry the values pinned by contracts/enums.json");
        }
    }

    private static bool IsSuccessStatus(string status) => status is "200" or "201" or "204";
}
