using GestorIA.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GestorIA.Api;

// Liveness and readiness (SPEC-009 §2). Both answer without the key (ApiKey.NeedsKey), so a client can tell "not running"
// from "running without its database" from "locked".
public static class Health
{
    // A database that does not answer within this long is as good as down for the one person waiting on the page.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);

    public static void MapHealth(this IEndpointRouteBuilder api)
    {
        var health = api.MapGroup("/health").WithTags("health");

        health.MapGet("/live", () => TypedResults.NoContent())
            .WithName("live")
            .WithSummary("The API process is running.");

        health.MapGet("/ready", Ready)
            .WithName("ready")
            .WithSummary("The API can serve requests: its database answers. 503 database-unavailable when it does not.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    // CanConnectAsync opens a connection and runs nothing else. It returns false rather than throw when the server cannot be
    // reached, and throws OperationCanceledException when the token fires first, which the timeout turns into "not ready".
    private static async Task<Results<NoContent, ProblemHttpResult>> Ready(GestoriaDbContext db, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadyTimeout);
        try
        {
            return await db.Database.CanConnectAsync(timeout.Token) ? TypedResults.NoContent() : Problems.NoDatabase();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problems.NoDatabase();
        }
    }
}
