using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace GestorIA.Api.Tests;

// The API with every log category captured at Trace, the most verbose level, for the tests that hold logs to SPEC-013. Each
// line starts with its level and category.
internal sealed class LoggedApi : ApiFactory
{
    private readonly Capture capture = new();

    internal LoggedApi()
    {
    }

    internal LoggedApi(string connectionString) : base(connectionString)
    {
    }

    internal IReadOnlyCollection<string> Lines => capture.Lines;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // A rule for one provider wins over the category levels of appsettings.json, so this one sees everything.
        builder.ConfigureLogging(logging => logging.AddProvider(capture).AddFilter<Capture>(null, LogLevel.Trace));
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
                lines.Enqueue($"{logLevel} {category} {formatter(state, exception)} {exception} {state}");
        }
    }
}
