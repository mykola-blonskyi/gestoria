using GestorIA.Api.SetAside;
using GestorIA.Engine;
using GestorIA.Infrastructure.Persistence;
using GestorIA.Infrastructure.Profiles;
using GestorIA.Infrastructure.SetAside;
using GestorIA.Infrastructure.TaxYears;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Api.Periods;

// Opening a quarter or the annual true-up of a stored profile's tax year (#71): each runs the same set-aside estimator as
// GET /profiles/{id}/set-aside/estimate and returns one period's own casillas, due window and trace.
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
        Guid id, string? quarter, GestoriaDbContext db, TaxYearConfigLoader loader, CancellationToken cancellationToken)
    {
        if (QuarterParameter.Parse(quarter) is not { } parsedQuarter)
        {
            return Problems.Invalid("quarter", QuarterParameter.InvalidMessage("quarter", quarter, "to open"));
        }

        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        var profile = row.ToProfile();
        try
        {
            var config = loader.Load(profile.TaxYear);
            var result = SetAsideEstimator.Estimate(profile.SetAsideInput(config, parsedQuarter));
            return TypedResults.Ok(QuarterResultView.From(result.Quarters.Single(q => q.Quarter == parsedQuarter), config.TaxYear, config.ConfigHash));
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
        Guid id, GestoriaDbContext db, TaxYearConfigLoader loader, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } row)
        {
            return Problems.NoProfile(id);
        }

        var profile = row.ToProfile();
        try
        {
            var config = loader.Load(profile.TaxYear);
            // Q4 is an arbitrary valid choice: the annual true-up does not depend on asOf.
            var result = SetAsideEstimator.Estimate(profile.SetAsideInput(config, Quarter.Q4));
            return TypedResults.Ok(AnnualTrueUpView.From(result.TrueUp, config.TaxYear, config.ConfigHash));
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
}
