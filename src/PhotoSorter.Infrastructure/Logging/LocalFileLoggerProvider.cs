using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PhotoSorter.Infrastructure.Logging;

public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private const long MaximumLogBytes = 2L * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly string _logPath;

    public LocalFileLoggerProvider(string cacheBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheBasePath);
        _logPath = Path.Combine(cacheBasePath, "logs", "photosorter.log");
    }

    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(this, categoryName);

    public void Dispose() => GC.SuppressFinalize(this);

    private void Write(
        string category,
        LogLevel level,
        string message,
        Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {category}: {message}";
        if (exception is not null)
        {
            line += $" ({exception.GetType().Name}: {exception.Message})";
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length >= MaximumLogBytes)
                {
                    RotateLogs();
                }

                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (Exception loggingException) when (loggingException is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"PhotoSorter logging failed: {loggingException.Message}");
            }
        }
    }

    private void RotateLogs()
    {
        var oldest = _logPath + ".3";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = 2; index >= 1; index--)
        {
            var source = _logPath + "." + index;
            if (File.Exists(source))
            {
                File.Move(source, _logPath + "." + (index + 1));
            }
        }

        File.Move(_logPath, _logPath + ".1");
    }

    private sealed class LocalFileLogger(
        LocalFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, formatter(state, exception), exception);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
