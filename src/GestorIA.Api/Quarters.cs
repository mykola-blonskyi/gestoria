using GestorIA.Engine;

namespace GestorIA.Api;

// Shared parsing for a Quarter read as text from a query string, so a missing or malformed value is an invalid-input
// problem naming the offending parameter, the same way across every endpoint that takes one (#71).
internal static class QuarterParameter
{
    public static Quarter? Parse(string? value) => value switch
    {
        "Q1" => Quarter.Q1,
        "Q2" => Quarter.Q2,
        "Q3" => Quarter.Q3,
        "Q4" => Quarter.Q4,
        _ => null,
    };

    // `what` fills the phrase "it must be the quarter {what}, one of Q1, Q2, Q3, Q4."; the default reproduces
    // ProfileEndpoints.Estimate's original wording for "asOf" byte for byte.
    public static string InvalidMessage(string parameterName, string? value, string what = "of the estimate") =>
        $"{parameterName} is {(value is null ? "missing" : $"\"{value}\"")}; it must be the quarter {what}, one of Q1, Q2, Q3, Q4.";
}
