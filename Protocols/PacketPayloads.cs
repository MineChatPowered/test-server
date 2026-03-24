using System.Formats.Cbor;

namespace Minechat.Server.Protocols;

public abstract record PacketPayload(CborConformanceMode ConformanceMode = CborConformanceMode.Canonical)
{
    public virtual void WritePayload(CborWriter writer) => writer.WriteNull();
    public static PacketPayload ReadPayload(CborReader reader, int packetType) => new EmptyPayload();
}

public record LinkPayload(string LinkingCode, string ClientUuid) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(2);
        writer.WriteInt32(0);
        writer.WriteTextString(LinkingCode);
        writer.WriteInt32(1);
        writer.WriteTextString(ClientUuid);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var linkingCode = "";
        var clientUuid = "";
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            switch (key)
            {
                case 0:
                    linkingCode = reader.ReadTextString() ?? "";
                    break;
                case 1:
                    clientUuid = reader.ReadTextString() ?? "";
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
        return new LinkPayload(linkingCode, clientUuid);
    }
}

public record LinkOkPayload(string MinecraftUuid) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(1);
        writer.WriteInt32(0);
        writer.WriteTextString(MinecraftUuid);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var minecraftUuid = "";
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            if (key == 0)
            {
                minecraftUuid = reader.ReadTextString() ?? "";
            }
            else
            {
                reader.SkipValue();
            }
        }
        reader.ReadEndMap();
        return new LinkOkPayload(minecraftUuid);
    }
}

public record CapabilitiesPayload(bool SupportsComponents) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(1);
        writer.WriteInt32(0);
        writer.WriteBoolean(SupportsComponents);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var supportsComponents = false;
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            if (key == 0)
            {
                supportsComponents = reader.ReadBoolean();
            }
            else
            {
                reader.SkipValue();
            }
        }
        reader.ReadEndMap();
        return new CapabilitiesPayload(supportsComponents);
    }
}

public record AuthOkPayload : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(0);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            reader.SkipValue();
            reader.SkipValue();
        }
        reader.ReadEndMap();
        return new AuthOkPayload();
    }
}

public record ChatMessagePayload(string Format, string Content) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(2);
        writer.WriteInt32(0);
        writer.WriteTextString(Format);
        writer.WriteInt32(1);
        writer.WriteTextString(Content);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var format = "";
        var content = "";
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            switch (key)
            {
                case 0:
                    format = reader.ReadTextString() ?? "";
                    break;
                case 1:
                    content = reader.ReadTextString() ?? "";
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
        return new ChatMessagePayload(format, content);
    }
}

public record PingPayload(long TimestampMs) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(1);
        writer.WriteInt32(0);
        writer.WriteInt64(TimestampMs);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var timestampMs = 0L;
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            if (key == 0)
            {
                timestampMs = reader.ReadInt64();
            }
            else
            {
                reader.SkipValue();
            }
        }
        reader.ReadEndMap();
        return new PingPayload(timestampMs);
    }
}

public record PongPayload(long TimestampMs) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(1);
        writer.WriteInt32(0);
        writer.WriteInt64(TimestampMs);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var timestampMs = 0L;
        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            if (key == 0)
            {
                timestampMs = reader.ReadInt64();
            }
            else
            {
                reader.SkipValue();
            }
        }
        reader.ReadEndMap();
        return new PongPayload(timestampMs);
    }
}

public record ModerationPayload(int Action, int Scope, string? Reason = null, int? DurationSeconds = null) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        var size = 2;
        if (Reason != null) size++;
        if (DurationSeconds != null) size++;

        writer.WriteStartMap(size);
        writer.WriteInt32(0);
        writer.WriteInt32(Action);
        writer.WriteInt32(1);
        writer.WriteInt32(Scope);
        if (Reason != null)
        {
            writer.WriteInt32(2);
            writer.WriteTextString(Reason);
        }
        if (DurationSeconds != null)
        {
            writer.WriteInt32(3);
            writer.WriteInt32(DurationSeconds.Value);
        }
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var action = 0;
        var scope = 0;
        string? reason = null;
        int? durationSeconds = null;

        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            switch (key)
            {
                case 0:
                    action = reader.ReadInt32();
                    break;
                case 1:
                    scope = reader.ReadInt32();
                    break;
                case 2:
                    reason = reader.ReadTextString();
                    break;
                case 3:
                    durationSeconds = reader.ReadInt32();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
        return new ModerationPayload(action, scope, reason, durationSeconds);
    }
}

public record SystemDisconnectPayload(int ReasonCode, string Message) : PacketPayload
{
    public override void WritePayload(CborWriter writer)
    {
        writer.WriteStartMap(2);
        writer.WriteInt32(0);
        writer.WriteInt32(ReasonCode);
        writer.WriteInt32(1);
        writer.WriteTextString(Message);
        writer.WriteEndMap();
    }

    public static new PacketPayload Read(CborReader reader)
    {
        var reasonCode = 0;
        var message = "";

        var count = reader.ReadStartMap();
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadInt32();
            switch (key)
            {
                case 0:
                    reasonCode = reader.ReadInt32();
                    break;
                case 1:
                    message = reader.ReadTextString() ?? "";
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
        return new SystemDisconnectPayload(reasonCode, message);
    }
}

public record EmptyPayload : PacketPayload;
