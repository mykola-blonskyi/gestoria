using System.Globalization;
using GestorIA.Engine;
using GestorIA.Infrastructure.SetAside;
using GestorIA.Infrastructure.TaxYears;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.OpenApi;

namespace GestorIA.Api.SetAside;

public static class SetAsideEndpoints
{
    public static void MapSetAside(this IEndpointRouteBuilder api)
    {
        api.MapPost("/set-aside/estimate", Estimate)
            .WithName("estimateSetAside")
            .WithTags("set-aside")
            .WithSummary("Runs the set-aside estimator on the console's input file for a tax year.")
            .Accepts<SetAsideInputDocument>("application/json")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                // The handler reads taxYear as text so that a missing or malformed year is an invalid-input problem like any
                // other refused value; the document still promises what it accepts, a required integer.
                var taxYear = (OpenApiParameter)operation.Parameters!.Single(p => p.Name == "taxYear");
                taxYear.Required = true;
                taxYear.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" };
                return Task.CompletedTask;
            });
    }

    // Results<A, B, C> lists every answer the handler can give, so the OpenAPI document shows each status with its body.
    private static async Task<Results<Ok<SetAsideEstimate>, ValidationProblem, ProblemHttpResult>> Estimate(
        string? taxYear, HttpRequest request, TaxYearConfigLoader loader)
    {
        if (!int.TryParse(taxYear, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return Problems.Invalid("taxYear", $"taxYear is {(taxYear is null ? "missing" : $"\"{taxYear}\"")}; it must be the tax year as a whole number, such as 2025.");
        }

        // The body is read as text and parsed by the console's own parser, which refuses duplicate and unknown fields.
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);

        try
        {
            var config = loader.Load(year);
            var input = SetAsideInputFile.Parse(body, config);
            return TypedResults.Ok(SetAsideEstimate.From(SetAsideEstimator.Estimate(input), config.TaxYear));
        }
        catch (InvalidInputFileException e)
        {
            return Problems.Invalid(e.Path, e.Message);
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
