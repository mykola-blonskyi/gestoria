using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace GestorIA.Api;

// Local mode's single-user key (SPEC-009 §3, ADR-0010). Configuration holds the key's SHA-256, never the key itself
// (SPEC-013 §2), from user secrets or the Auth__ApiKeySha256 environment variable; web/README.md shows how to set it.
public sealed class ApiKeyOptions
{
    public string? ApiKeySha256 { get; set; }
}

public static class ApiKey
{
    public const string Header = "X-Api-Key";
    private const string Scheme = "apiKey";

    // What `printf %s "$KEY" | shasum -a 256` prints when $KEY is unset: a hash that would let an empty key in.
    private const string EmptyKeySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    // ValidateOnStart makes the API refuse to start without a usable hash, rather than start open or lock everyone out.
    public static void AddApiKey(this IServiceCollection services)
    {
        var options = services.AddOptions<ApiKeyOptions>()
            .BindConfiguration("Auth")
            .Validate(
                o => o.ApiKeySha256 is { Length: 64 } hash && hash.All(char.IsAsciiHexDigit) && !hash.Equals(EmptyKeySha256, StringComparison.OrdinalIgnoreCase),
                "Auth:ApiKeySha256 must be the SHA-256 of a non-empty local API key, as 64 hex characters (web/README.md, \"The API key\").");
        if (!OpenApiGeneration.IsRunning)
        {
            options.ValidateOnStart();
        }
    }

    // Declares the key on every endpoint of the group in the OpenAPI document. UseApiKey enforces it by path, not by this.
    public static RouteGroupBuilder RequireApiKey(this RouteGroupBuilder group)
    {
        group.WithMetadata(new RequiresApiKey()).ProducesProblem(StatusCodes.Status401Unauthorized);
        return group;
    }

    // app.Use adds inline middleware, code that runs on every request. The rule is the path, not the endpoint routing chose:
    // a locked route asked with the wrong method or Content-Type matches no endpoint and would otherwise answer 405 or 415,
    // telling a caller without the key which routes exist. It runs before parameter binding, so a request without the key
    // is refused before the API reads anything else from it. CORS preflights never get here: UseCors answers them first.
    public static void UseApiKey(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (!NeedsKey(context.Request.Path) || HasKey(context))
            {
                await next(context);
                return;
            }

            context.Response.Headers.WWWAuthenticate = $"ApiKey header=\"{Header}\"";
            await Problems.Unauthorized().ExecuteAsync(context);
        });

    // The health checks stay open so that a client can tell "not running" from "locked" (SPEC-009 §3).
    private static bool NeedsKey(PathString path) =>
        path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase) && !path.StartsWithSegments("/api/v1/health", StringComparison.OrdinalIgnoreCase);

    // Both sides are hashed, so FixedTimeEquals compares two 32-byte values and the time taken says nothing about the key.
    private static bool HasKey(HttpContext context)
    {
        var presented = context.Request.Headers[Header];
        var expected = context.RequestServices.GetRequiredService<IOptions<ApiKeyOptions>>().Value.ApiKeySha256;
        return presented.Count == 1
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented[0]!)), Convert.FromHexString(expected!));
    }

    public static void AddApiKeySecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Scheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = Header,
                Description = "The local installation's API key (SPEC-009 §3). The health checks do not need it.",
            };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<RequiresApiKey>().Any())
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Scheme, context.Document)] = [] }];
            }
            return Task.CompletedTask;
        });
    }

    private sealed record RequiresApiKey;
}
