namespace ReturnColors;

internal enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

internal sealed record LogEntry(LogLevel Level, string Message, DateTime Timestamp);

internal interface ILogger
{
    void Info(string? message);
    void Success(string? message);
    void Warning(string? message);
    void Error(string? message);
}

internal sealed class ConsoleLogger : ILogger
{
    private static void Write(string? message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = previous;
    }

    public void Info(string? message) => Write(message, ConsoleColor.Gray);
    public void Success(string? message) => Write(message, ConsoleColor.Green);
    public void Warning(string? message) => Write(message, ConsoleColor.Yellow);
    public void Error(string? message) => Write(message, ConsoleColor.Red);
}

internal static class Log
{
    private static ILogger _logger = new ConsoleLogger();

    public static ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static event Action<LogEntry>? EntryReceived;

    public static void Info(string? message)
    {
        _logger.Info(message);
        EntryReceived?.Invoke(new LogEntry(LogLevel.Info, message ?? string.Empty, DateTime.Now));
    }

    public static void Success(string? message)
    {
        _logger.Success(message);
        EntryReceived?.Invoke(new LogEntry(LogLevel.Success, message ?? string.Empty, DateTime.Now));
    }

    public static void Warning(string? message)
    {
        _logger.Warning(message);
        EntryReceived?.Invoke(new LogEntry(LogLevel.Warning, message ?? string.Empty, DateTime.Now));
    }

    public static void Error(string? message)
    {
        _logger.Error(message);
        EntryReceived?.Invoke(new LogEntry(LogLevel.Error, message ?? string.Empty, DateTime.Now));
    }
}
