namespace Minechat.Server.Logging;

public interface IChatLogger
{
    void LogChat(string sender, string content);
}
