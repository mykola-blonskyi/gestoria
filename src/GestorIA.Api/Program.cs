using System.Text.Json.Serialization;
using GestorIA.Api;
using GestorIA.Api.SetAside;
using GestorIA.Api.TaxYears;
using GestorIA.Infrastructure.TaxYears;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddApiKey();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddSchemaTransformer<UnionSchemas>();
    options.AddApiKeySecurity();
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "GestorIA API";
        document.Info.Description = "SPEC-009. Money is a string with two decimals, dates are ISO-8601, errors are RFC 9457 problem+json.";
        return Task.CompletedTask;
    });
});

// The tax-year files are copied next to the binary (GestorIA.Api.csproj); TaxYears:Directory points elsewhere.
var taxYears = builder.Configuration["TaxYears:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "config", "tax-years");
builder.Services.AddSingleton(new TaxYearConfigLoader(taxYears));

// The web app runs on its own origin next to the API (ADR-0010, web/README.md).
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).WithMethods("GET", "POST").WithHeaders("Content-Type", "Accept", ApiKey.Header)));

var app = builder.Build();

// A request Kestrel or binding refuses, such as a body over the size limit, throws BadHttpRequestException; it keeps its status.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = e => e is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseCors();
app.UseApiKey();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api/v1");
api.MapGet("/health/live", () => TypedResults.NoContent()).WithName("live").WithTags("health");

var locked = api.MapGroup("").RequireApiKey();
locked.MapTaxYears();
locked.MapSetAside();

app.Run();

// WebApplicationFactory<Program> in GestorIA.Api.Tests needs a public type to name; top-level statements make Program internal.
public partial class Program;
