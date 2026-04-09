using System.Text;
using AwesomeAssertions;
using NUnit.Framework;
using Minechat.Server.Compression;
using Minechat.Server.Framing;
using Minechat.Server.Protocols;

namespace Minechat.Server.Tests;

public class CompressionHandlerTests
{
    private readonly ICompressionHandler _handler = new CliCompressor();

    [Test]
    public void Compress_ThenDecompress_ReturnsOriginalData()
    {
        var original = "Hello, World! This is a test message for compression.";
        var originalBytes = Encoding.UTF8.GetBytes(original);

        var compressed = _handler.Compress(originalBytes);
        var decompressed = _handler.Decompress(compressed, originalBytes.Length);

        decompressed.Should().Equal(originalBytes);
    }

    [Test]
    public void Compress_EmptyData_ReturnsCompressedData()
    {
        var originalBytes = Array.Empty<byte>();

        var compressed = _handler.Compress(originalBytes);
        var decompressed = _handler.Decompress(compressed, 0);

        decompressed.Should().BeEmpty();
    }

    [Test]
    public void Compress_LargeData_WorksCorrectly()
    {
        var original = new string('A', 10000);
        var originalBytes = Encoding.UTF8.GetBytes(original);

        var compressed = _handler.Compress(originalBytes);
        var decompressed = _handler.Decompress(compressed, originalBytes.Length);

        decompressed.Should().Equal(originalBytes);
    }

    [Test]
    public void Compress_ProducesSmallerOutput_ForRepetitiveData()
    {
        var original = new string('X', 1000);
        var originalBytes = Encoding.UTF8.GetBytes(original);

        var compressed = _handler.Compress(originalBytes);

        compressed.Length.Should().BeLessThan(originalBytes.Length);
    }
}

public class FrameHandlerTests
{
    private readonly FrameHandler _handler = new();

    [Test]
    public async Task WriteFrame_ThenReadFrame_ReturnsCorrectData()
    {
        using var ms = new MemoryStream();
        var originalData = Encoding.UTF8.GetBytes("Test frame data");
        var compressed = new byte[] { 1, 2, 3, 4, 5 };

        await _handler.WriteFrameAsync(ms, compressed, originalData.Length);

        ms.Position = 0;
        var result = await _handler.ReadFrameAsync(ms);

        result.Should().NotBeNull();
        result!.Value.decompressedSize.Should().Be(originalData.Length);
        result.Value.compressedSize.Should().Be(5);
        result.Value.compressedData.Should().Equal(compressed);
    }

    [Test]
    public async Task ReadFrame_InvalidSize_ThrowsException()
    {
        using var ms = new MemoryStream();
        byte[] invalidHeader = [0, 0, 0, 0, 0, 0, 0, 0];
        ms.Write(invalidHeader);

        ms.Position = 0;
        var action = () => _handler.ReadFrameAsync(ms);

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Test]
    public async Task ReadFrame_SizeExceedsMax_ThrowsException()
    {
        using var ms = new MemoryStream();
        var header = new byte[8];
        // Use big-endian: 2MB = 2097152
        header[0] = 0x00; header[1] = 0x20; header[2] = 0x00; header[3] = 0x00;
        header[4] = 0x00; header[5] = 0x00; header[6] = 0x00; header[7] = 0x64;
        ms.Write(header);

        ms.Position = 0;
        var action = () => _handler.ReadFrameAsync(ms);

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Test]
    public async Task ReadFrame_TruncatedData_ThrowsException()
    {
        using var ms = new MemoryStream();
        var header = new byte[8];
        // Use big-endian for header values
        header[0] = 0; header[1] = 0; header[2] = 0; header[3] = 100;
        header[4] = 0; header[5] = 0; header[6] = 0; header[7] = 50;
        ms.Write(header);
        ms.Write(new byte[30]);

        ms.Position = 0;
        var action = () => _handler.ReadFrameAsync(ms);

        await action.Should().ThrowAsync<EndOfStreamException>();
    }
}

public class PacketSerializationTests
{
    [Test]
    public void LinkPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new LinkPayload("TEST123", "client-uuid-123");
        var packet = new MineChatPacket(PacketTypes.LINK, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.LINK);
        var linkPayload = deserialized.Payload as LinkPayload;
        linkPayload.Should().NotBeNull();
        linkPayload!.LinkingCode.Should().Be("TEST123");
        linkPayload.ClientUuid.Should().Be("client-uuid-123");
    }

    [Test]
    public void LinkOkPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new LinkOkPayload("minecraft-uuid-abc");
        var packet = new MineChatPacket(PacketTypes.LINK_OK, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.LINK_OK);
        var linkOk = deserialized.Payload as LinkOkPayload;
        linkOk.Should().NotBeNull();
        linkOk!.MinecraftUuid.Should().Be("minecraft-uuid-abc");
    }

    [Test]
    public void CapabilitiesPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new CapabilitiesPayload(new[] { "components", "commonmark" }, "commonmark");
        var packet = new MineChatPacket(PacketTypes.CAPABILITIES, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.CAPABILITIES);
        var caps = deserialized.Payload as CapabilitiesPayload;
        caps.Should().NotBeNull();
        caps!.SupportedFormats.Should().Contain("components");
        caps.SupportedFormats.Should().Contain("commonmark");
        caps.PreferredFormat.Should().Be("commonmark");
    }

    [Test]
    public void AuthOkPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new AuthOkPayload();
        var packet = new MineChatPacket(PacketTypes.AUTH_OK, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.AUTH_OK);
        deserialized.Payload.Should().BeOfType<AuthOkPayload>();
    }

    [Test]
    public void ChatMessagePayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new ChatMessagePayload("commonmark", "Hello **world**!");
        var packet = new MineChatPacket(PacketTypes.CHAT_MESSAGE, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.CHAT_MESSAGE);
        var chat = deserialized.Payload as ChatMessagePayload;
        chat.Should().NotBeNull();
        chat!.Format.Should().Be("commonmark");
        chat.Content.Should().Be("Hello **world**!");
    }

    [Test]
    public void ChatMessagePayload_ComponentsFormat_Serialize_ThenDeserialize_Matches()
    {
        var payload = new ChatMessagePayload("components", "{\"text\":\"Hello\"}");
        var packet = new MineChatPacket(PacketTypes.CHAT_MESSAGE, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        var chat = deserialized!.Payload as ChatMessagePayload;
        chat.Should().NotBeNull();
        chat!.Format.Should().Be("components");
        chat.Content.Should().Be("{\"text\":\"Hello\"}");
    }

    [Test]
    public void PingPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new PingPayload(1234567890123L);
        var packet = new MineChatPacket(PacketTypes.PING, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.PING);
        var ping = deserialized.Payload as PingPayload;
        ping.Should().NotBeNull();
        ping!.TimestampMs.Should().Be(1234567890123L);
    }

    [Test]
    public void PongPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new PongPayload(1234567890123L);
        var packet = new MineChatPacket(PacketTypes.PONG, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.PONG);
        var pong = deserialized.Payload as PongPayload;
        pong.Should().NotBeNull();
        pong!.TimestampMs.Should().Be(1234567890123L);
    }

    [Test]
    public void ModerationPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new ModerationPayload(ModerationAction.KICK, ModerationScope.CLIENT, "No spam", 300);
        var packet = new MineChatPacket(PacketTypes.MODERATION, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.MODERATION);
        var mod = deserialized.Payload as ModerationPayload;
        mod.Should().NotBeNull();
        mod!.Action.Should().Be(ModerationAction.KICK);
        mod.Scope.Should().Be(ModerationScope.CLIENT);
        mod.Reason.Should().Be("No spam");
        mod.DurationSeconds.Should().Be(300);
    }

    [Test]
    public void ModerationPayload_OptionalFields_Serialize_ThenDeserialize_Matches()
    {
        var payload = new ModerationPayload(ModerationAction.WARN, ModerationScope.ACCOUNT);
        var packet = new MineChatPacket(PacketTypes.MODERATION, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        var mod = deserialized!.Payload as ModerationPayload;
        mod.Should().NotBeNull();
        mod!.Action.Should().Be(ModerationAction.WARN);
        mod.Scope.Should().Be(ModerationScope.ACCOUNT);
        mod.Reason.Should().BeNull();
        mod.DurationSeconds.Should().BeNull();
    }

    [Test]
    public void SystemDisconnectPayload_Serialize_ThenDeserialize_Matches()
    {
        var payload = new SystemDisconnectPayload(SystemDisconnectReason.SHUTDOWN, "Server shutting down");
        var packet = new MineChatPacket(PacketTypes.SYSTEM_DISCONNECT, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.PacketType.Should().Be(PacketTypes.SYSTEM_DISCONNECT);
        var disc = deserialized.Payload as SystemDisconnectPayload;
        disc.Should().NotBeNull();
        disc!.ReasonCode.Should().Be(SystemDisconnectReason.SHUTDOWN);
        disc.Message.Should().Be("Server shutting down");
    }

    [Test]
    public void Deserialize_UnknownPacketType_ReturnsNull()
    {
        var payload = new ChatMessagePayload("commonmark", "test");
        var packet = new MineChatPacket(0xFF, payload);

        var serialized = packet.Serialize();
        var deserialized = MineChatPacket.Deserialize(serialized);

        deserialized.Should().BeNull();
    }

    [Test]
    public void Deserialize_InvalidData_ReturnsNull()
    {
        var invalidData = new byte[] { 1, 2, 3, 4, 5 };
        var result = MineChatPacket.Deserialize(invalidData);

        result.Should().BeNull();
    }
}

public class PacketTypesTests
{
    [Test]
    public void PacketTypeValues_AreCorrect()
    {
        PacketTypes.LINK.Should().Be(0x01);
        PacketTypes.LINK_OK.Should().Be(0x02);
        PacketTypes.CAPABILITIES.Should().Be(0x03);
        PacketTypes.AUTH_OK.Should().Be(0x04);
        PacketTypes.CHAT_MESSAGE.Should().Be(0x05);
        PacketTypes.PING.Should().Be(0x06);
        PacketTypes.PONG.Should().Be(0x07);
        PacketTypes.MODERATION.Should().Be(0x08);
        PacketTypes.SYSTEM_DISCONNECT.Should().Be(0x09);
    }

    [Test]
    public void ModerationActionValues_AreCorrect()
    {
        ModerationAction.WARN.Should().Be(0);
        ModerationAction.MUTE.Should().Be(1);
        ModerationAction.KICK.Should().Be(2);
        ModerationAction.BAN.Should().Be(3);
    }

    [Test]
    public void ModerationScopeValues_AreCorrect()
    {
        ModerationScope.CLIENT.Should().Be(0);
        ModerationScope.ACCOUNT.Should().Be(1);
    }

    [Test]
    public void SystemDisconnectReasonValues_AreCorrect()
    {
        SystemDisconnectReason.SHUTDOWN.Should().Be(0);
        SystemDisconnectReason.MAINTENANCE.Should().Be(1);
        SystemDisconnectReason.INTERNAL_ERROR.Should().Be(2);
        SystemDisconnectReason.OVERLOADED.Should().Be(3);
    }
}