using System.Formats.Cbor;

namespace Minechat.Server.Protocols;

public record MineChatPacket(int PacketType, PacketPayload Payload)
{
    private const CborConformanceMode ConformanceMode = CborConformanceMode.Canonical;

    public byte[] Serialize()
    {
        var writer = new CborWriter(ConformanceMode);

        writer.WriteStartMap(2);
        writer.WriteInt32(0);
        writer.WriteInt32(PacketType);
        writer.WriteInt32(1);
        Payload.WritePayload(writer);
        writer.WriteEndMap();

        var data = writer.Encode();
        return data;
    }

    public static MineChatPacket? Deserialize(byte[] data)
    {
        try
        {
            var reader = new CborReader(data, ConformanceMode);

            var count = reader.ReadStartMap();

            int packetType = 0;
            PacketPayload? payload = null;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var key = reader.ReadUInt32();

                    if (key == 0)
                    {
                        packetType = (int)reader.ReadUInt32();
                    }
                    else if (key == 1)
                    {
                        payload = ReadPayload(reader, packetType);
                    }
                    else
                    {
                        reader.SkipValue();
                    }
                }
                catch
                {
                    try { reader.SkipValue(); } catch { }
                    try { packetType = (int)reader.ReadUInt32(); } catch { }
                }
            }
            reader.ReadEndMap();

            if (payload == null)
                return null;

            return new MineChatPacket(packetType, payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to deserialize packet: {ex.Message}");
            return null;
        }
    }

    private static PacketPayload ReadPayload(CborReader reader, int packetType)
    {
        return packetType switch
        {
            PacketTypes.LINK => LinkPayload.Read(reader),
            PacketTypes.LINK_OK => LinkOkPayload.Read(reader),
            PacketTypes.CAPABILITIES => CapabilitiesPayload.Read(reader),
            PacketTypes.AUTH_OK => AuthOkPayload.Read(reader),
            PacketTypes.CHAT_MESSAGE => ChatMessagePayload.Read(reader),
            PacketTypes.PING => PingPayload.Read(reader),
            PacketTypes.PONG => PongPayload.Read(reader),
            PacketTypes.MODERATION => ModerationPayload.Read(reader),
            PacketTypes.SYSTEM_DISCONNECT => SystemDisconnectPayload.Read(reader),
            _ => new EmptyPayload()
        };
    }
}
