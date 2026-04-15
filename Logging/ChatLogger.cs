using Serilog;

namespace Minechat.Server.Logging;

public class ChatLogger : IChatLogger
{
    private readonly string _chatLogPath;
    private readonly ILogger _logger;

    public ChatLogger(string chatLogPath = "chat.log")
    {
        _chatLogPath = chatLogPath;
        _logger = Log.ForContext<ChatLogger>();
    }

    public void LogChat(string sender, string content)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var logLine = $"{timestamp} [{sender}] {content}";

        _logger.Information("[CHAT] {Sender}: {Content}", sender, content);

        try
        {
            var directory = Path.GetDirectoryName(_chatLogPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.AppendAllText(_chatLogPath, logLine + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to write chat message to file");
        }
    }
}
