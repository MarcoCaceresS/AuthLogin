using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuthService.Tests.Infrastructure;

/// <summary>ApiFactory que guarda los logs para revisarlos en el test.</summary>
public sealed class LogCaptureApiFactory : ApiFactory
{
    public ConcurrentQueue<(string Category, LogLevel Level)> Logs { get; } = new();

    protected override void ConfigureExtra(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(new Provider(Logs)));

    private sealed class Provider(ConcurrentQueue<(string, LogLevel)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string category) => new Logger(category, logs);
        public void Dispose() { }
    }

    private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel)> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f) =>
            logs.Enqueue((category, level));
    }
}
