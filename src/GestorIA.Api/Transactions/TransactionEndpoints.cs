using System.Globalization;
using System.Text.Json.Nodes;
using GestorIA.Domain.Interfaces;
using GestorIA.Engine;
using GestorIA.Infrastructure.Parsers;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace GestorIA.Api.Transactions;

// Bank statements imported into the stored profile, and the movements they hold (SPEC-009 §2, #72).
public static class TransactionEndpoints
{
    // The statement adapters by the id a client names in ?bank= (SPEC-004 §5).
    private static readonly IReadOnlyDictionary<string, IStatementParser> Parsers =
        new IStatementParser[] { new BbvaCsvStatementParser() }.ToDictionary(parser => parser.Bank, StringComparer.Ordinal);

    public static void MapTransactions(this IEndpointRouteBuilder api)
    {
        // Every endpoint here reads PostgreSQL, so each may answer 503 database-unavailable (DatabaseUnavailable).
        var profile = api.MapGroup("/profiles/{id:guid}").WithTags("transactions").ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        profile.MapPost("/bank-statements", Import)
            .WithName("importBankStatement")
            .WithSummary("Imports a bank statement, the file itself as the body, for the period from and to state or else its lines' first to last date. A line an earlier import stored is not stored again.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                var bank = (OpenApiParameter)operation.Parameters!.Single(p => p.Name == "bank");
                bank.Required = true;
                bank.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [.. Parsers.Keys.Select(key => (JsonNode)key)] };
                // Read as text so a malformed date is an invalid-input problem; the document still says what each accepts.
                foreach (var end in operation.Parameters!.Cast<OpenApiParameter>().Where(p => p.Name is "from" or "to"))
                {
                    end.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" };
                    end.Description = end.Name == "from"
                        ? "The statement period's first day; the first line's booking date when left out."
                        : "The statement period's last day; the last line's booking date when left out.";
                }

                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Description = $"The statement file as exported by the bank, at most {StatementFile.MaxBytes} bytes.",
                    Content = StatementFile.MediaTypes.ToDictionary(
                        type => type,
                        _ => new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String } }),
                };
                return Task.CompletedTask;
            });

        profile.MapGet("/bank-statements", Statements)
            .WithName("listBankStatements")
            .WithSummary("The imported statements, each with the period it covers, in the order of their periods.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        profile.MapGet("/transactions", List)
            .WithName("listTransactions")
            .WithSummary("The stored movements in booking-date order: all of them, a year's, or a quarter's of that year.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                // Read as text so a malformed filter is an invalid-input problem; the document still says what each accepts.
                var parameters = operation.Parameters!.Cast<OpenApiParameter>().ToDictionary(p => p.Name!);
                parameters["year"].Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" };
                parameters["quarter"].Schema = new OpenApiSchemaReference(nameof(Quarter), context.Document);
                parameters["quarter"].Description = "Needs year.";
                return Task.CompletedTask;
            });

        profile.MapGet("/review-queue", ReviewQueue)
            .WithName("getReviewQueue")
            .WithSummary("The movements of the profile's tax year waiting for the user's class, unclear or with a rule's suggestion, in the transactions list's order.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/transactions/{id:guid}/classify", Classify)
            .WithName("classifyTransaction")
            .WithTags("transactions")
            .WithSummary("Stores the user's class for a movement. A later call replaces an earlier one.")
            .Accepts<TransactionClassification>("application/json")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<BankStatementImport>, ValidationProblem, ProblemHttpResult>> Import(
        Guid id, string? bank, string? from, string? to, HttpRequest request, GestoriaDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (bank is null || !Parsers.ContainsKey(bank))
        {
            errors["bank"] = [$"bank is {(bank is null ? "missing" : $"\"{bank}\"")}; it must name the statement's format, one of {string.Join(", ", Parsers.Keys)}."];
        }

        var (declaredFrom, declaredTo) = (Date("from", from, errors), Date("to", to, errors));
        if (errors.Count > 0)
        {
            return Problems.Invalid(errors);
        }

        var parser = Parsers[bank!];

        if (!StatementFile.IsAcceptedMediaType(request.ContentType))
        {
            return Problems.UnsupportedStatementType(StatementFile.MediaTypes);
        }

        if (!await db.Profiles.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return Problems.NoProfile(id);
        }

        if (await RequestBody.ReadAtMostAsync(request, StatementFile.MaxBytes) is not { } bytes)
        {
            return Problems.TooLarge(StatementFile.MaxBytes);
        }

        IReadOnlyList<(StatementLine Line, string Key)> statement;
        try
        {
            statement = LineKeys.Of(parser.Parse(StatementFile.Decode(bytes)));
        }
        catch (InvalidStatementException e)
        {
            return Problems.Invalid(e.Errors.ToDictionary(error => error.Key, error => error.Value));
        }

        // The period of every line the file holds, the ones an earlier import stored included: they are this statement's too.
        var dates = statement.Select(line => line.Line.Movement.BookingDate).ToList();
        var (first, last) = dates.Count == 0 ? ((DateOnly?)null, (DateOnly?)null) : (dates.Min(), dates.Max());
        var (periodFrom, periodTo) = (declaredFrom ?? first ?? default, declaredTo ?? last ?? default);
        var refusals = StatementPeriod.Refusals(periodFrom, periodTo, first, last, MadridDay.Of(clock.GetUtcNow()));
        if (refusals.Count > 0)
        {
            return Problems.Invalid(refusals.ToDictionary(refusal => refusal.End ?? "file", refusal => new[] { $"{refusal.End ?? "The file"} {refusal.Reason}." }));
        }

        // Imports into one profile run one at a time: each holds the profile's row lock from reading which lines are stored to
        // storing the rest, so two overlapping statements imported at once cannot both find a line missing. The unique index on
        // the line key stays the last word.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // FromSql sends each {hole} of the interpolated string as a SQL parameter, never as text pasted into the query.
        var locked = await db.Profiles.FromSql($"SELECT * FROM \"Profiles\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            return Problems.NoProfile(id);
        }

        var keys = statement.Select(line => line.Key).ToList();
        var stored = await db.BankTransactions
            .Where(t => t.ProfileId == id && keys.Contains(t.LineKey))
            .Select(t => t.LineKey)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);
        var fresh = statement.Where(line => !stored.Contains(line.Key)).ToList();
        // Under the profile's lock, so no other import of this profile can take the same number. Recorded even when every line
        // was stored before: the statement's period is news of its own.
        var sequence = await db.StatementImports.Where(i => i.ProfileId == id).MaxAsync(i => (int?)i.Sequence, cancellationToken) + 1 ?? 1;
        db.StatementImports.Add(new StatementImportRow { ProfileId = id, Sequence = sequence, From = periodFrom, To = periodTo });
        db.BankTransactions.AddRange(fresh.Select(line => new BankTransactionRow
        {
            Id = Guid.NewGuid(),
            ProfileId = id,
            LineKey = line.Key,
            ImportSequence = sequence,
            LineNumber = line.Line.Number,
            BookingDate = line.Line.Movement.BookingDate,
            ValueDate = line.Line.Movement.ValueDate,
            Description = line.Line.Movement.Description,
            Amount = line.Line.Movement.Amount.Amount,
            Balance = line.Line.Movement.Balance?.Amount,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(new BankStatementImport(parser.Bank, statement.Count, fresh.Count, statement.Count - fresh.Count, periodFrom, periodTo));
    }

    private static async Task<Results<Ok<List<StatementImportView>>, ProblemHttpResult>> Statements(
        Guid id, GestoriaDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Profiles.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return Problems.NoProfile(id);
        }

        var imports = await db.StatementImports.AsNoTracking()
            .Where(i => i.ProfileId == id)
            .OrderBy(i => i.From).ThenBy(i => i.To).ThenBy(i => i.Sequence)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(imports.Select(StatementImportView.Of).ToList());
    }

    // An end of a stated period, as yyyy-MM-dd; null when it is not given, or not a date, which errors then names.
    private static DateOnly? Date(string end, string? text, Dictionary<string, string[]> errors)
    {
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[end] = [$"{end} is \"{text}\"; it must be a date written yyyy-MM-dd, such as 2025-03-31."];
        return null;
    }

    private static async Task<Results<Ok<List<TransactionView>>, ValidationProblem, ProblemHttpResult>> List(
        Guid id, string? year, string? quarter, GestoriaDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        int? parsedYear = null;
        if (year is not null)
        {
            if (int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1 and <= 9999)
            {
                parsedYear = y;
            }
            else
            {
                errors["year"] = [$"year is \"{year}\"; it must be a year as a whole number, such as 2025."];
            }
        }

        Quarter? parsedQuarter = quarter switch
        {
            null => null,
            "Q1" => Quarter.Q1,
            "Q2" => Quarter.Q2,
            "Q3" => Quarter.Q3,
            "Q4" => Quarter.Q4,
            _ => null,
        };
        if (quarter is not null && parsedQuarter is null)
        {
            errors["quarter"] = [$"quarter is \"{quarter}\"; it must be one of Q1, Q2, Q3, Q4."];
        }
        else if (quarter is not null && year is null)
        {
            errors["quarter"] = ["quarter needs year: Q1 of which year?"];
        }

        if (errors.Count > 0)
        {
            return Problems.Invalid(errors);
        }

        if (!await db.Profiles.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return Problems.NoProfile(id);
        }

        var rows = db.BankTransactions.AsNoTracking().Where(t => t.ProfileId == id);
        if (parsedYear is { } from)
        {
            var (first, last) = parsedQuarter is { } q
                ? (new DateOnly(from, (int)q * 3 - 2, 1), new DateOnly(from, (int)q * 3, 1).AddMonths(1).AddDays(-1))
                : (new DateOnly(from, 1, 1), new DateOnly(from, 12, 31));
            rows = rows.Where(t => t.BookingDate >= first && t.BookingDate <= last);
        }

        var found = await rows.InListOrder().ToListAsync(cancellationToken);
        return TypedResults.Ok(found.Select(TransactionView.From).ToList());
    }

    // Scoped to the profile's tax year: the transactions page and the estimate work on it, and a line of another year changes
    // nothing the app computes.
    private static async Task<Results<Ok<List<ReviewItem>>, ProblemHttpResult>> ReviewQueue(
        Guid id, GestoriaDbContext db, TransactionRules rules, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } profile)
        {
            return Problems.NoProfile(id);
        }

        var rows = await db.BankTransactions.AsNoTracking().OfTaxYear(id, profile.TaxYear).ToListAsync(cancellationToken);
        return TypedResults.Ok(rows
            .Select(row => (Row: row, Classification: row.ClassifiedBy(rules)))
            .Where(line => line.Classification is not Classification.Confirmed)
            .Select(line => ReviewItem.From(line.Row, line.Classification))
            .ToList());
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> Classify(
        Guid id, HttpRequest request, GestoriaDbContext db, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        if (ClassifyInput.Refusal(await reader.ReadToEndAsync(cancellationToken), out var decided) is { } refused)
        {
            return refused;
        }

        // Only a line whose profile is this installation's: the route names no profile. ExecuteUpdateAsync sends one UPDATE
        // without loading the row and answers how many rows it changed.
        var updated = await db.BankTransactions
            .Where(t => t.Id == id && db.Profiles.Any(p => p.Id == t.ProfileId))
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Class, decided), cancellationToken);
        return updated == 0 ? Problems.NoTransaction(id) : TypedResults.NoContent();
    }
}
