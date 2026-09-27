using System.Text.Json.Serialization;
using GestorIA.Api;
using GestorIA.Api.Profiles;
using GestorIA.Api.SetAside;
using GestorIA.Api.TaxYears;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.TaxYears;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    // A union's "kind" member may come anywhere in its object, not only first (ProfileInputDocument).
    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseUnavailable>();
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

// PostgreSQL (ADR-0006), started locally by compose.yaml (ADR-0010). The connection string holds a password, so it comes from
// user secrets or the ConnectionStrings__Gestoria environment variable, never a file in the repository (README.md, "Database").
// EF Core's three failure events log the exception, whose message names the database's host and port (SPEC-013 §2). The
// exception reaches UseExceptionHandler anyway: DatabaseUnavailable answers 503 without logging it, and anything else is
// logged there as a 500, so ignoring the events loses nothing. The Debug-level connection and data-reader events name the
// database and its server on every connection, so they go too.
builder.Services.AddDbContext<GestoriaDbContext>((services, options) => options
    .UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("Gestoria")
            ?? throw new InvalidOperationException("ConnectionStrings:Gestoria is not set; README.md, \"Database\", shows how to set it."))
    .ConfigureWarnings(events => events.Ignore(
        RelationalEventId.ConnectionError,
        CoreEventId.QueryIterationFailed,
        CoreEventId.SaveChangesFailed,
        RelationalEventId.ConnectionOpening,
        RelationalEventId.ConnectionOpened,
        RelationalEventId.ConnectionClosing,
        RelationalEventId.ConnectionClosed,
        RelationalEventId.ConnectionDisposing,
        RelationalEventId.ConnectionDisposed,
        RelationalEventId.DataReaderClosing,
        RelationalEventId.DataReaderDisposing,
        RelationalEventId.MigrateUsingConnection)));

// The web app runs on its own origin next to the API (ADR-0010, web/README.md).
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT").WithHeaders("Content-Type", "Accept", ApiKey.Header)));

var app = builder.Build();

// Brings the database to the latest migration before the first request. Migrate is idempotent: a database already there is
// left as it is.
if (!OpenApiGeneration.IsRunning)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<GestoriaDbContext>().Database.MigrateAsync();
}

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
api.MapHealth();

var locked = api.MapGroup("").RequireApiKey();
locked.MapTaxYears();
locked.MapSetAside();
locked.MapProfiles();

app.Run();

// WebApplicationFactory<Program> in GestorIA.Api.Tests needs a public type to name; top-level statements make Program internal.
public partial class Program;
