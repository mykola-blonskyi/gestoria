using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace GestorIA.Api.Tests;

// SPEC-013: logs never hold a secret. Every category is captured at Trace, the most verbose level, while the key is used,
// mistyped and left out, and no line may contain the key or the wrong one.
public class ApiKeyNotLogged
{
    [Fact]
    public async Task NeitherTheKeyNorAWrongOneReachesAnyLog()
    {
        const string wrongKey = "mistyped-key-for-the-log-test";
        using var api = new LoggedApi();

        await api.CreateClient().GetAsync("/api/v1/config/tax-years");
        await api.CreateClient().Estimate(2025, RepoFiles.GoldenInput("G14"));
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config/tax-years");
        wrong.Headers.Add(ApiKey.Header, wrongKey);
        await api.CreateClientWithoutKey().SendAsync(wrong);
        await api.CreateClientWithoutKey().GetAsync("/api/v1/config/tax-years");

        Assert.NotEmpty(api.Lines);
        Assert.DoesNotContain(api.Lines, line => line.Contains(ApiFactory.Key, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Lines, line => line.Contains(wrongKey, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Lines, line => line.Contains(ApiFactory.Sha256(ApiFactory.Key), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LoggedApi : ApiFactory
    {
        private readonly Capture capture = new();

        internal IReadOnlyCollection<string> Lines => capture.Lines;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // A rule for one provider wins over the category levels of appsettings.json, so this one sees everything.
            builder.ConfigureLogging(logging => logging.AddProvider(capture).AddFilter<Capture>(null, LogLevel.Trace));
        }
    }

    private sealed class Capture : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> lines = new();

        internal IReadOnlyCollection<string> Lines => lines;

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, lines);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{category} {formatter(state, exception)} {exception} {state}");
        }
    }
}
