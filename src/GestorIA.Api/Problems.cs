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
    public const string ProfileNotFound = Base + "profile-not-found";
    public const string ProfileExists = Base + "profile-exists";
    public const string StatementTooLarge = Base + "statement-too-large";
    public const string StatementMediaType = Base + "statement-media-type";
    public const string DatabaseUnavailable = Base + "database-unavailable";
    public const string NoUpcomingObligations = Base + "no-upcoming-obligations";

    // 400 with the ValidationProblemDetails "errors" member, keyed by the JSON path of the offending value.
    public static ValidationProblem Invalid(string path, string message) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { [path] = [message] },
            detail: message,
            title: "The input is not valid",
            type: InvalidInput);

    // 400 naming every refused field of a request at once, each by its JSON path.
    public static ValidationProblem Invalid(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, detail: string.Join(" ", errors.Values.SelectMany(reasons => reasons)), title: "The input is not valid", type: InvalidInput);

    public static ProblemHttpResult NoProfile(Guid id) =>
        TypedResults.Problem($"There is no profile {id}.", statusCode: StatusCodes.Status404NotFound, title: "No such profile", type: ProfileNotFound);

    // 409: local mode keeps one profile (SPEC-009 §1.1); a second one is a PUT to the first.
    public static ProblemHttpResult OneProfileOnly(Guid existing) =>
        TypedResults.Problem(
            $"This installation already holds profile {existing}; change it with PUT /api/v1/profiles/{existing}.",
            statusCode: StatusCodes.Status409Conflict,
            title: "A profile already exists",
            type: ProfileExists);

    // 413: a bank statement over the upload limit, refused before any of it is read as text.
    public static ProblemHttpResult TooLarge(int maxBytes) =>
        TypedResults.Problem(
            $"A statement may be at most {maxBytes} bytes; export a shorter period.",
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "The statement is too large",
            type: StatementTooLarge);

    // 415: a statement sent as something other than a CSV file, such as a form upload or JSON.
    public static ProblemHttpResult UnsupportedStatementType(IEnumerable<string> accepted) =>
        TypedResults.Problem(
            $"Send the statement file itself as the body, with Content-Type {string.Join(", ", accepted)}.",
            statusCode: StatusCodes.Status415UnsupportedMediaType,
            title: "The statement is not sent as a CSV file",
            type: StatementMediaType);

    // 422: a tax year, a region or a value the configuration does not carry, or declares not published yet (SPEC-007 §3).
    public static ProblemHttpResult Gap(string reason) =>
        TypedResults.Problem(reason, statusCode: StatusCodes.Status422UnprocessableEntity, title: "The configuration does not cover this calculation", type: ConfigGap);

    // 422: a calendar export with no obligation due today or later. RFC 5545 §3.4 requires at least one component, so an empty
    // VCALENDAR is not a valid file, and a calendar app would import nothing from it without saying so.
    public static ProblemHttpResult NoUpcoming(int taxYear, DateOnly today) =>
        TypedResults.Problem(
            FormattableString.Invariant($"Every obligation of tax year {taxYear} was due before {today:yyyy-MM-dd}; there is nothing still to come to export."),
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "No obligation is still to come",
            type: NoUpcomingObligations);

    // 422: an input the parser accepts but the engine cannot estimate, such as actuals out of order.
    public static ProblemHttpResult Refused(string reason) =>
        TypedResults.Problem(reason, statusCode: StatusCodes.Status422UnprocessableEntity, title: "The engine cannot estimate this input", type: EstimateRefused);

    // 401: no key, more than one, or not the configured one. The answer does not say which.
    public static ProblemHttpResult Unauthorized() =>
        TypedResults.Problem($"Send the local API key in the {ApiKey.Header} header.", statusCode: StatusCodes.Status401Unauthorized, title: "The request needs the local API key", type: ApiKeyRequired);

    // 503: PostgreSQL cannot be reached (DatabaseUnavailable, /health/ready). The same fixed words every time: what Npgsql
    // says names the host, the port and the database, and none of it belongs in an answer (SPEC-013 §2).
    public static ProblemHttpResult NoDatabase() =>
        TypedResults.Problem(
            "The API is running but cannot reach its database. Start PostgreSQL (docker compose up -d postgres) and try again.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The database is not reachable",
            type: DatabaseUnavailable);
}
