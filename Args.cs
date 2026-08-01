using CommandLine;

namespace MineChat.Server;

public class Args
{
    [Option(Default = DefaultServerConfig.DefaultPort)]
    public ushort Port { get; set; }
}
