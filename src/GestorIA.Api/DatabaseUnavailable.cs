using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace GestorIA.Api;

// Turns "PostgreSQL cannot be reached" into a 503 database-unavailable problem instead of an unhandled 500 (SPEC-009 §4).
// UseExceptionHandler calls every registered IExceptionHandler in turn; returning true means handled, and since .NET 10 the
// middleware then writes no log of its own. The exception is never logged here either: Npgsql's message names the host, the
// port and the database (SPEC-013 §2), so the one line written says only that the database is down.
public sealed class DatabaseUnavailable(ILogger<DatabaseUnavailable> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (!Is(exception))
        {
            return false;
        }

        logger.LogWarning("The database is not reachable; the request was answered 503.");
        await Problems.NoDatabase().ExecuteAsync(context);
        return true;
    }

    // Anywhere in the chain, since EF Core wraps what Npgsql throws (a DbUpdateException on save). A PostgresException is an
    // answer from a running server, such as a unique violation, and counts only when it is about the connection: class 08, or
    // the server shutting down or starting up (57P01–57P03). Any other NpgsqlException is Npgsql failing to reach the server
    // or losing it mid-request.
    public static bool Is(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case PostgresException postgres when postgres.SqlState.StartsWith("08", StringComparison.Ordinal) || postgres.SqlState is PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown or PostgresErrorCodes.CannotConnectNow:
                    return true;
                case PostgresException:
                    return false;
                case NpgsqlException:
                    return true;
            }
        }
        return false;
    }
}
