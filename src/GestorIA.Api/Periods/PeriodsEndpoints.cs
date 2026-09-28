using GestorIA.Api.SetAside;
using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.SetAside;
using GestorIA.Infrastructure.TaxYears;
using GestorIA.Infrastructure.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Api.Periods;

// Opening a quarter or the annual true-up of a stored profile's tax year (#71): each runs the same set-aside estimator as
// GET /profiles/{id}/set-aside/estimate, on the same actuals from the classified movements (#73), and returns one period's own
// casillas, due window and trace, the ledger's steps first.
public static class PeriodsEndpoints
{
    public static void MapPeriods(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/profiles").WithTags("periods");

        group.MapPost("/{id:guid}/calculations/quarter", QuarterCalculation)
            .WithName("calculateQuarter")
            .WithSummary("Runs the set-aside estimator on a stored profile and returns one quarter's Modelo 130, its casillas and trace.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/calculations/annual-true-up", AnnualTrueUp)
            .WithName("calculateAnnualTrueUp")
            .WithSummary("Runs the set-aside estimator on a stored profile and returns the annual true-up gap, due window and trace.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<QuarterResultView>, ValidationProblem, ProblemHttpResult>> QuarterCalculation(
        Guid id, string? quarter, GestoriaDbContext db, TaxYearConfigLoader loader, TransactionRules rules, TimeProvider time, CancellationToken cancellationToken)
    {
        if (QuarterParameter.Parse(quarter) is not { } parsedQuarter)
        {
            return Problems.Invalid("quarter", QuarterParameter.InvalidMessage("quarter", quarter, "to open"));
        }

        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        try
        {
            var config = loader.Load(row.TaxYear);
            var ledger = await db.SetAsideInputAsync(rules, row, config, parsedQuarter, Today(time), cancellationToken);
            var result = SetAsideEstimator.Estimate(ledger.Input);
            return TypedResults.Ok(QuarterResultView.From(result.Quarters.Single(q => q.Quarter == parsedQuarter), config.TaxYear, config.ConfigHash, ledger));
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

    private static async Task<Results<Ok<AnnualTrueUpView>, ProblemHttpResult>> AnnualTrueUp(
        Guid id, GestoriaDbContext db, TaxYearConfigLoader loader, TransactionRules rules, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        try
        {
            var config = loader.Load(row.TaxYear);
            // Q4, so every closed quarter of the year can give its actuals; the engine's true-up itself does not depend on asOf.
            var ledger = await db.SetAsideInputAsync(rules, row, config, Quarter.Q4, Today(time), cancellationToken);
            var result = SetAsideEstimator.Estimate(ledger.Input);
            return TypedResults.Ok(AnnualTrueUpView.From(result.TrueUp, config.TaxYear, config.ConfigHash, ledger));
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

    private static DateOnly Today(TimeProvider time) => MadridDay.Of(time.GetUtcNow());
}
