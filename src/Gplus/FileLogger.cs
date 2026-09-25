using System.Text;

namespace Gplus;

/// <summary>Minimal daily-rolling text log in %ProgramData%\Gplus\logs (the Event Log gets errors too).</summary>
public sealed class FileLoggerProvider(string folder) : ILoggerProvider
{
    private readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"gplus-{DateTime.Now:yyyy-MM-dd}.log"), line, Encoding.UTF8);
            }
            catch (IOException) { /* logging must never take the service down */ }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        private readonly string _short = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ')
                .Append(logLevel.ToString()[..4].ToUpperInvariant()).Append(' ')
                .Append(_short).Append(": ").Append(formatter(state, exception)).AppendLine();
            if (exception is not null) sb.AppendLine(exception.ToString());
            owner.Write(sb.ToString());
        }
    }
}
