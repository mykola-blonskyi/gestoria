using System.Globalization;
using GestorIA.Api.SetAside;
using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.SetAside;
using GestorIA.Infrastructure.TaxYears;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Npgsql;

namespace GestorIA.Api.Profiles;

// The taxpayer profile, entered once in settings and read by the dashboard's estimate (SPEC-009 §2, #69). Local mode keeps
// one profile per installation: GET /profiles lists it, or nothing before the first POST.
public static class ProfileEndpoints
{
    public static void MapProfiles(this IEndpointRouteBuilder api)
    {
        // Every profile endpoint reads PostgreSQL, so each may answer 503 database-unavailable (DatabaseUnavailable).
        var profiles = api.MapGroup("/profiles").WithTags("profiles").ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        profiles.MapGet("/", List)
            .WithName("listProfiles")
            .WithSummary("The stored profiles: none before the first is created, one after.");

        profiles.MapPost("/", Create)
            .WithName("createProfile")
            .WithSummary("Stores the profile. Local mode keeps one: a second is refused with 409.")
            .Accepts<ProfileInputDocument>("application/json")
            .ProducesProblem(StatusCodes.Status409Conflict);

        profiles.MapGet("/{id:guid}", Get)
            .WithName("getProfile")
            .WithSummary("One stored profile.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapPut("/{id:guid}", Replace)
            .WithName("replaceProfile")
            .WithSummary("Replaces a stored profile with the one in the body.")
            .Accepts<ProfileInputDocument>("application/json")
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapDelete("/{id:guid}", Delete)
            .WithName("deleteProfile")
            .WithSummary("Deletes the profile and everything stored for it. Nothing is kept: export it first to keep a copy.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapGet("/{id:guid}/export", Export)
            .WithName("exportProfile")
            .WithSummary("Everything stored for the profile, in one versioned document (SPEC-009 §2.1). Personal financial data: sent with Cache-Control: no-store.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapGet("/{id:guid}/set-aside/estimate", Estimate)
            .WithName("estimateSetAsideForProfile")
            .WithSummary("Runs the set-aside estimator on a stored profile for a quarter of its tax year, with no closed quarter stated.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                // Read as text so a missing or malformed quarter is an invalid-input problem; the document still says which.
                var asOf = (OpenApiParameter)operation.Parameters!.Single(p => p.Name == "asOf");
                asOf.Required = true;
                asOf.Schema = new OpenApiSchemaReference(nameof(Quarter), context.Document);
                return Task.CompletedTask;
            });
    }

    private static async Task<Ok<List<ProfileView>>> List(GestoriaDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Profiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(rows.Select(View).ToList());
    }

    private static async Task<Results<Created<ProfileView>, ValidationProblem, ProblemHttpResult>> Create(
        HttpRequest request, GestoriaDbContext db, TaxYearConfigLoader loader, IOptions<JsonOptions> json, CancellationToken cancellationToken)
    {
        Profile profile;
        try
        {
            profile = ProfileInput.Parse(await ProfileInput.ReadAsync(request, json.Value.SerializerOptions), loader);
        }
        catch (InvalidProfileException e)
        {
            return Problems.Invalid(e.Errors);
        }

        if (await db.Profiles.Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
        {
            return Problems.OneProfileOnly(existing);
        }

        var row = ProfileRow.From(profile);
        db.Profiles.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another create won the race between the look above and this insert; the unique index on Singleton refused this one.
            db.ChangeTracker.Clear();
            return Problems.OneProfileOnly(await db.Profiles.Select(p => p.Id).SingleAsync(cancellationToken));
        }

        return TypedResults.Created($"/api/v1/profiles/{row.Id}", View(row));
    }

    private static async Task<Results<Ok<ProfileView>, ProblemHttpResult>> Get(Guid id, GestoriaDbContext db, CancellationToken cancellationToken) =>
        await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } row
            ? TypedResults.Ok(View(row))
            : Problems.NoProfile(id);

    private static async Task<Results<Ok<ProfileView>, ValidationProblem, ProblemHttpResult>> Replace(
        Guid id, HttpRequest request, GestoriaDbContext db, TaxYearConfigLoader loader, IOptions<JsonOptions> json, CancellationToken cancellationToken)
    {
        if (await db.Profiles.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        try
        {
            row.Write(ProfileInput.Parse(await ProfileInput.ReadAsync(request, json.Value.SerializerOptions), loader));
        }
        catch (InvalidProfileException e)
        {
            return Problems.Invalid(e.Errors);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(View(row));
    }

    // The rows that belong to the profile go with it through their foreign keys' ON DELETE CASCADE, in the same statement.
    // ProfileDeletion enumerates every table of the model to prove it, so a table added without the cascade fails that test.
    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, GestoriaDbContext db, CancellationToken cancellationToken) =>
        await db.Profiles.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken) == 0
            ? Problems.NoProfile(id)
            : TypedResults.NoContent();

    private static async Task<Results<Ok<ProfileExport>, ProblemHttpResult>> Export(
        Guid id, HttpResponse response, GestoriaDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        var exportedAt = DateTimeOffset.UtcNow;
        // No shared cache or browser cache may keep a copy of personal financial data (SPEC-013). The file name says what the
        // file is and when it was made, and nothing about whose it is.
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = string.Create(CultureInfo.InvariantCulture, $"attachment; filename=\"gestoria-export-{exportedAt:yyyy-MM-dd}.json\"");
        return TypedResults.Ok(ProfileExport.Of(View(row), exportedAt));
    }

    private static async Task<Results<Ok<SetAsideEstimate>, ValidationProblem, ProblemHttpResult>> Estimate(
        Guid id, string? asOf, GestoriaDbContext db, TaxYearConfigLoader loader, CancellationToken cancellationToken)
    {
        Quarter? parsed = asOf switch
        {
            "Q1" => Quarter.Q1,
            "Q2" => Quarter.Q2,
            "Q3" => Quarter.Q3,
            "Q4" => Quarter.Q4,
            _ => null,
        };
        if (parsed is not { } quarter)
        {
            return Problems.Invalid("asOf", $"asOf is {(asOf is null ? "missing" : $"\"{asOf}\"")}; it must be the quarter of the estimate, one of Q1, Q2, Q3, Q4.");
        }

        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        var profile = row.ToProfile();
        try
        {
            var config = loader.Load(profile.TaxYear);
            return TypedResults.Ok(SetAsideEstimate.From(SetAsideEstimator.Estimate(profile.SetAsideInput(config, quarter)), config.TaxYear));
        }
        catch (ConfigNotFoundException e)
        {
            return Problems.Gap(e.Message);
        }
        catch (Exception e) when (EngineRefusal.Is(e))
        {
            return Problems.Refused(EngineRefusal.Reason(e));
        }
    }

    private static ProfileView View(ProfileRow row) => ProfileView.From(row.Id, row.ToProfile());
}
