using Microsoft.AspNetCore.Http.HttpResults;

namespace GestorIA.Api;

// RFC 9457 problem types (SPEC-009 §4). The URIs name the kind of failure for a client to switch on; they are identifiers,
// not pages. Detail carries the reason in words and may quote an amount, so a client shows it and never logs it (SPEC-013).
public static class Problems
{
    private const string Base = "https://gestoria.local/problems/";

    public const string InvalidInput = Base + "invalid-input";
    public const string ConfigGap = Base + "config-gap";
    public const string EstimateRefused = Base + "estimate-refused";
    public const string TaxYearNotFound = Base + "tax-year-not-found";
    public const string ApiKeyRequired = Base + "api-key-required";

    // 400 with the ValidationProblemDetails "errors" member, keyed by the JSON path of the offending value.
    public static ValidationProblem Invalid(string path, string message) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { [path] = [message] },
            detail: message,
            title: "The input is not valid",
            type: InvalidInput);

    // 422: a tax year, a region or a value the configuration does not carry, or declares not published yet (SPEC-007 §3).
    public static ProblemHttpResult Gap(string reason) =>
        TypedResults.Problem(reason, statusCode: StatusCodes.Status422UnprocessableEntity, title: "The configuration does not cover this calculation", type: ConfigGap);

    // 422: an input the parser accepts but the engine cannot estimate, such as actuals out of order.
    public static ProblemHttpResult Refused(string reason) =>
        TypedResults.Problem(reason, statusCode: StatusCodes.Status422UnprocessableEntity, title: "The engine cannot estimate this input", type: EstimateRefused);

    // 401: no key, more than one, or not the configured one. The answer does not say which.
    public static ProblemHttpResult Unauthorized() =>
        TypedResults.Problem($"Send the local API key in the {ApiKey.Header} header.", statusCode: StatusCodes.Status401Unauthorized, title: "The request needs the local API key", type: ApiKeyRequired);
}
