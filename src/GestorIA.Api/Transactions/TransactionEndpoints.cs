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
        var profile = api.MapGroup("/profiles/{id:guid}").WithTags("transactions");

        profile.MapPost("/bank-statements", Import)
            .WithName("importBankStatement")
            .WithSummary("Imports a bank statement, the file itself as the body. A line an earlier import stored is not stored again.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                var bank = (OpenApiParameter)operation.Parameters!.Single(p => p.Name == "bank");
                bank.Required = true;
                bank.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [.. Parsers.Keys.Select(key => (JsonNode)key)] };
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
    }

    private static async Task<Results<Ok<BankStatementImport>, ValidationProblem, ProblemHttpResult>> Import(
        Guid id, string? bank, HttpRequest request, GestoriaDbContext db, CancellationToken cancellationToken)
    {
        if (bank is null || !Parsers.TryGetValue(bank, out var parser))
        {
            return Problems.Invalid("bank", $"bank is {(bank is null ? "missing" : $"\"{bank}\"")}; it must name the statement's format, one of {string.Join(", ", Parsers.Keys)}.");
        }

        if (!StatementFile.IsAcceptedMediaType(request.ContentType))
        {
            return Problems.UnsupportedStatementType(StatementFile.MediaTypes);
        }

        if (!await db.Profiles.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return Problems.NoProfile(id);
        }

        if (await StatementFile.ReadAsync(request) is not { } bytes)
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
        // Under the profile's lock, so no other import of this profile can take the same number.
        var sequence = await db.BankTransactions.Where(t => t.ProfileId == id).MaxAsync(t => (int?)t.ImportSequence, cancellationToken) + 1 ?? 1;
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

        return TypedResults.Ok(new BankStatementImport(parser.Bank, statement.Count, fresh.Count, statement.Count - fresh.Count));
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

        var found = await rows.OrderBy(t => t.BookingDate).ThenBy(t => t.ImportSequence).ThenBy(t => t.LineNumber).ToListAsync(cancellationToken);
        return TypedResults.Ok(found.Select(TransactionView.From).ToList());
    }
}
