using GestorIA.Engine;
using GestorIA.Infrastructure.TaxYears;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GestorIA.Api.TaxYears;

public static class TaxYearEndpoints
{
    public static void MapTaxYears(this IEndpointRouteBuilder api)
    {
        var taxYears = api.MapGroup("/config/tax-years").WithTags("config");

        taxYears.MapGet("/", List)
            .WithName("listTaxYears")
            .WithSummary("Every tax year the API holds a configuration for, oldest first.");

        taxYears.MapGet("/{year:int}", Get)
            .WithName("getTaxYear")
            .WithSummary("One tax year's configuration: its hash, regions and declared gaps.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Ok<List<TaxYearView>> List(TaxYearConfigLoader loader) =>
        TypedResults.Ok(loader.Years().Select(year => TaxYearView.From(loader.Load(year))).ToList());

    private static Results<Ok<TaxYearView>, ProblemHttpResult> Get(int year, TaxYearConfigLoader loader)
    {
        try
        {
            return TypedResults.Ok(TaxYearView.From(loader.Load(year)));
        }
        catch (ConfigNotFoundException e)
        {
            return TypedResults.Problem(e.Message, statusCode: StatusCodes.Status404NotFound, title: "No configuration for this tax year", type: Problems.TaxYearNotFound);
        }
    }
}
