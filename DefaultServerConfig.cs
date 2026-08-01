namespace MineChat.Server;

public static class DefaultServerConfig
{
    public const ushort DefaultPort = 7632;
    public const int MaxClients = 100;

    public const int PingIntervalSeconds = 15;
    public const int KeepAliveTimeoutSeconds = 15;
    public const int ConnectionTimeoutSeconds = 60;

    public const string LogFilePath = "logs/server.log";
}
