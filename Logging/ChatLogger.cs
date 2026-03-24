namespace Minechat.Server.Logging;

public class ChatLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public ChatLogger(string logFilePath = "chat.log")
    {
        _logFilePath = logFilePath;
    }

    public void LogChat(string clientUuid, string message)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var logLine = $"{timestamp} [{clientUuid}] {message}";

        Console.WriteLine(logLine);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    public void LogConnection(string clientUuid, string eventType)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var logLine = $"{timestamp} [{eventType}] {clientUuid}";

        Console.WriteLine(logLine);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    public void LogError(string message, Exception? ex = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var logLine = ex != null
            ? $"{timestamp} [ERROR] {message}: {ex.Message}"
            : $"{timestamp} [ERROR] {message}";

        Console.WriteLine(logLine);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch (Exception _) { }
        }
    }
}
