namespace Minechat.Server;

public static class ServerConfig
{
    public const int DEFAULT_PORT = 7632;
    public const int MAX_CLIENTS = 100;

    public const int PING_INTERVAL_SECONDS = 10;
    public const int KEEP_ALIVE_TIMEOUT_SECONDS = 15;
    public const int CONNECTION_TIMEOUT_SECONDS = 60;
    public const int TLS_HANDSHAKE_TIMEOUT_SECONDS = 10;

    public const string LOG_FILE_PATH = "logs/server.log";
}
