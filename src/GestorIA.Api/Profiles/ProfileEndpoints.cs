using System.Text.Json;
using System.Text.Json.Nodes;
using GestorIA.Api.SetAside;
using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.SetAside;
using GestorIA.Infrastructure.TaxYears;
using GestorIA.Infrastructure.Transactions;
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

        profiles.MapPost("/restore", Restore)
            .WithName("restoreProfile")
            .WithSummary("Restores an export (SPEC-009 §2.1) into an empty installation. The same file again changes nothing; anything else stored is a 409.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                // Declared here and not with Accepts, which has routing answer any other Content-Type with a bare 415 before
                // the handler can answer export-media-type.
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Description = $"An export file (SPEC-009 §2.1), at most {ProfileRestore.MaxBytes} bytes.",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = new OpenApiSchemaReference(nameof(ProfileExport), context.Document) },
                    },
                };
                return Task.CompletedTask;
            });

        profiles.MapGet("/{id:guid}/set-aside/estimate", Estimate)
            .WithName("estimateSetAsideForProfile")
            .WithSummary("Runs the set-aside estimator on a stored profile for a quarter of its tax year, its classified movements giving the actuals of the closed quarters.")
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

        profiles.MapGet("/{id:guid}/calendar", Calendar)
            .WithName("calendarForProfile")
            .WithSummary("Every obligation of the profile's tax year, in due-date order: Modelo 130, 303 and 349, the monthly TGSS cuota and the Renta true-up.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        profiles.MapGet("/{id:guid}/calendar.ics", CalendarIcs)
            .WithName("calendarForProfileAsIcs")
            .WithSummary("The upcoming calendar as an RFC 5545 export; amounts=true adds each obligation's known amount to its title, lang picks the event text's language.")
            .Produces<string>(StatusCodes.Status200OK, "text/calendar")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                // Read as text so a malformed amounts or lang is an invalid-input problem; the document still says what each accepts.
                var amounts = (OpenApiParameter)operation.Parameters!.Single(p => p.Name == "amounts");
                amounts.Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean };
                var lang = (OpenApiParameter)operation.Parameters!.Single(p => p.Name == "lang");
                lang.Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [.. PaymentsCalendarIcsText.SupportedLocales.Select(l => (JsonNode)JsonValue.Create(l))],
                };
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
        if (await Stored(id, db, cancellationToken) is not { } export)
        {
            return Problems.NoProfile(id);
        }

        // No shared cache or browser cache may keep a copy of personal financial data (SPEC-013).
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = $"attachment; filename=\"{ProfileExport.FileName(export.ExportedAt)}\"";
        return TypedResults.Ok(export);
    }

    // Local mode keeps one profile per installation, so a restore fills an empty one, or answers for what is already there:
    // the file's own contents are a 200 that writes nothing, so a retry is safe, and anything else a 409 (SPEC-009 §2.2).
    private static async Task<Results<Created<RestoredExport>, Ok<RestoredExport>, ValidationProblem, ProblemHttpResult>> Restore(
        HttpRequest request, GestoriaDbContext db, TaxYearConfigLoader loader, IOptions<JsonOptions> json, CancellationToken cancellationToken)
    {
        if (!ProfileRestore.IsJson(request.ContentType))
        {
            return Problems.UnsupportedExportType();
        }

        if (await RequestBody.ReadAtMostAsync(request, ProfileRestore.MaxBytes) is not { } body)
        {
            return Problems.TooLargeExport(ProfileRestore.MaxBytes);
        }

        Restored file;
        try
        {
            file = ProfileRestore.Read(body, json.Value.SerializerOptions, loader);
        }
        catch (InvalidExportException e)
        {
            return Problems.Invalid(e.Errors);
        }

        // A second pass only after a concurrent restore stored its profile first: the unique index on Singleton refused this
        // one, and the installation is no longer empty.
        for (var pass = 1; ; pass++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // FOR SHARE holds off a delete until this answer is made, so the stored profile and its movements are read as one.
            var held = await db.Profiles.FromSql($"SELECT * FROM \"Profiles\" FOR SHARE").AsNoTracking().ToListAsync(cancellationToken);
            if (held is [var profile])
            {
                var stored = (await Stored(profile.Id, db, cancellationToken))!.Entities;
                var counts = EntityCounts.Of(stored);
                var options = json.Value.SerializerOptions;
                return JsonSerializer.Serialize(stored, options) == JsonSerializer.Serialize(file.Entities, options)
                    ? TypedResults.Ok(new RestoredExport(profile.Id, counts))
                    : Problems.InstallationHolds(profile.Id, profile.TaxYear, counts);
            }

            db.Profiles.Add(file.Profile);
            db.BankTransactions.AddRange(file.BankTransactions);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return TypedResults.Created($"/api/v1/profiles/{file.Profile.Id}", new RestoredExport(file.Profile.Id, EntityCounts.Of(file.Entities)));
            }
            catch (DbUpdateException e) when (pass == 1 && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
    }

    // What is stored for a profile, as the export writes it. The restore compares a file with the same answer.
    private static async Task<ProfileExport?> Stored(Guid id, GestoriaDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return null;
        }

        var transactions = await db.BankTransactions.AsNoTracking()
            .Where(t => t.ProfileId == id)
            .OrderBy(t => t.BookingDate).ThenBy(t => t.ImportSequence).ThenBy(t => t.LineNumber)
            .ToListAsync(cancellationToken);
        return ProfileExport.Of(View(row), transactions, DateTimeOffset.UtcNow);
    }

    private static async Task<Results<Ok<SetAsideEstimate>, ValidationProblem, ProblemHttpResult>> Estimate(
        Guid id, string? asOf, GestoriaDbContext db, TaxYearConfigLoader loader, TransactionRules rules, TimeProvider time, CancellationToken cancellationToken)
    {
        if (QuarterParameter.Parse(asOf) is not { } quarter)
        {
            return Problems.Invalid("asOf", QuarterParameter.InvalidMessage("asOf", asOf));
        }

        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        try
        {
            var config = loader.Load(row.TaxYear);
            var ledger = await db.SetAsideInputAsync(rules, row, config, quarter, MadridDay.Of(time.GetUtcNow()), cancellationToken);
            return TypedResults.Ok(SetAsideEstimate.From(SetAsideEstimator.Estimate(ledger.Input), config.TaxYear, ledger));
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

    private static async Task<Results<Ok<PaymentsCalendarView>, ProblemHttpResult>> Calendar(
        Guid id, GestoriaDbContext db, TaxYearConfigLoader loader, TransactionRules rules, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        try
        {
            var config = loader.Load(row.TaxYear);
            var obligations = await Obligations(db, rules, row, config, MadridDay.Of(clock.GetUtcNow()), cancellationToken);
            return TypedResults.Ok(PaymentsCalendarView.From(obligations, config));
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

    private static async Task<Results<ContentHttpResult, ValidationProblem, ProblemHttpResult>> CalendarIcs(
        Guid id, string? amounts, string? lang, GestoriaDbContext db, TaxYearConfigLoader loader, TransactionRules rules, TimeProvider clock, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var includeAmounts = false;
        if (amounts is not null && !bool.TryParse(amounts, out includeAmounts))
        {
            errors["amounts"] = [$"amounts is \"{amounts}\"; it must be \"true\" or \"false\"."];
        }

        var locale = lang ?? PaymentsCalendarIcsText.DefaultLocale;
        if (lang is not null && !PaymentsCalendarIcsText.SupportedLocales.Contains(lang))
        {
            errors["lang"] = [$"lang is \"{lang}\"; it must be one of {string.Join(", ", PaymentsCalendarIcsText.SupportedLocales)}."];
        }

        if (errors.Count > 0)
        {
            return Problems.Invalid(errors);
        }

        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        try
        {
            var config = loader.Load(row.TaxYear);
            var today = MadridDay.Of(clock.GetUtcNow());
            var obligations = await Obligations(db, rules, row, config, today, cancellationToken);
            // The export holds what is still upcoming, not the profile's whole tax year (#70 AC): a calendar app is for what
            // comes next, and past obligations already show on the page.
            var upcoming = obligations.Where(o => o.DueWindow.End >= today).ToList();
            if (upcoming.Count == 0)
            {
                return Problems.NoUpcoming(config.TaxYear, today);
            }

            return TypedResults.Text(PaymentsCalendarIcs.Write(upcoming, config.TaxYear, id, includeAmounts, locale), "text/calendar");
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

    // The whole year's obligations, at asOf Q4 so every closed quarter can give its actuals (#73). A quarter's Modelo 130 is
    // the one POST /calculations/quarter answers for it: the actuals up to that quarter are the same run of closed, reviewed
    // quarters whichever asOf reads them, and a quarter's payment depends only on the figures up to it.
    private static async Task<IReadOnlyList<PaymentObligation>> Obligations(
        GestoriaDbContext db, TransactionRules rules, ProfileRow row, TaxYearConfig config, DateOnly today, CancellationToken cancellationToken) =>
        PaymentCalendar.Build((await db.SetAsideInputAsync(rules, row, config, Quarter.Q4, today, cancellationToken)).Input);

    private static ProfileView View(ProfileRow row) => ProfileView.From(row.Id, row.ToProfile());
}
