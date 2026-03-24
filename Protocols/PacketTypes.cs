namespace Minechat.Server.Protocols;

public static class PacketTypes
{
    public const int LINK = 0x01;
    public const int LINK_OK = 0x02;
    public const int CAPABILITIES = 0x03;
    public const int AUTH_OK = 0x04;
    public const int CHAT_MESSAGE = 0x05;
    public const int PING = 0x06;
    public const int PONG = 0x07;
    public const int MODERATION = 0x08;
    public const int SYSTEM_DISCONNECT = 0x09;
}

public static class ModerationAction
{
    public const int WARN = 0;
    public const int MUTE = 1;
    public const int KICK = 2;
    public const int BAN = 3;
}

public static class ModerationScope
{
    public const int CLIENT = 0;
    public const int ACCOUNT = 1;
}

public static class SystemDisconnectReason
{
    public const int SHUTDOWN = 0;
    public const int MAINTENANCE = 1;
    public const int INTERNAL_ERROR = 2;
    public const int OVERLOADED = 3;
}
