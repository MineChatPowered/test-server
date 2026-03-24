using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Minechat.Server.Compression;
using Minechat.Server.Framing;
using Minechat.Server.Logging;
using Minechat.Server.Protocols;

namespace Minechat.Server.Connection;

public class ClientConnection
{
    private readonly TcpClient _client;
    private readonly SslStream _sslStream;
    private readonly ChatLogger _logger;
    private readonly FrameHandler _frameHandler;
    private readonly CompressionHandler _compressionHandler;
    private bool _running = true;
    private string? _clientUuid;
    private string? _minecraftUuid;
    private bool _authenticated;

    public ClientConnection(TcpClient client, X509Certificate2 serverCert, ChatLogger logger)
    {
        _client = client;
        _sslStream = new SslStream(
            _client.GetStream(),
            false,
            (sender, certificate, chain, errors) => true
        );
        _logger = logger;
        _frameHandler = new FrameHandler();
        _compressionHandler = new CompressionHandler();

        try
        {
            _sslStream.AuthenticateAsServer(serverCert);
            _logger.LogConnection("unknown", "TLS_HANDSHAKE_SUCCESS");
        }
        catch (Exception ex)
        {
            _logger.LogError("TLS handshake failed", ex);
            throw;
        }
    }

    public async Task RunAsync()
    {
        try
        {
            while (_running)
            {
                var frame = await _frameHandler.ReadFrameAsync(_sslStream);
                if (frame == null)
                    break;

                var (decompressedSize, _, compressedData) = frame.Value;

                var decompressed = _compressionHandler.Decompress(compressedData, decompressedSize);
                var packet = MineChatPacket.Deserialize(decompressed);

                if (packet == null)
                {
                    Console.WriteLine($"DEBUG: Failed to deserialize. Data hex: {Convert.ToHexString(decompressed.Take(64).ToArray())}");
                    _logger.LogError("Failed to deserialize packet");
                    break;
                }

                Console.WriteLine($"DEBUG: Successfully deserialized packet type {packet.PacketType}");

                await HandlePacketAsync(packet);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Connection error", ex);
        }
        finally
        {
            Close();
        }
    }

    private async Task HandlePacketAsync(MineChatPacket packet)
    {
        switch (packet.PacketType)
        {
            case PacketTypes.LINK:
                await HandleLinkAsync(packet.Payload as LinkPayload);
                break;
            case PacketTypes.CAPABILITIES:
                await HandleCapabilitiesAsync(packet.Payload as CapabilitiesPayload);
                break;
            case PacketTypes.CHAT_MESSAGE:
                HandleChatMessage(packet.Payload as ChatMessagePayload);
                break;
            case PacketTypes.PING:
                await HandlePingAsync(packet.Payload as PingPayload);
                break;
            case PacketTypes.PONG:
                HandlePong(packet.Payload as PongPayload);
                break;
        }
    }

    private async Task HandleLinkAsync(LinkPayload? payload)
    {
        if (payload == null) return;

        _clientUuid = payload.ClientUuid;
        var linkingCode = payload.LinkingCode;

        _logger.LogConnection(_clientUuid, $"LINK_RECEIVED code={linkingCode}");

        var responsePayload = new LinkOkPayload("550e8400-e29b-41d4-a716-446655440000");
        await SendPacketAsync(PacketTypes.LINK_OK, responsePayload);

        _logger.LogConnection(_clientUuid, "LINK_OK_SENT");
    }

    private async Task HandleCapabilitiesAsync(CapabilitiesPayload? payload)
    {
        if (payload == null) return;

        _logger.LogConnection(_clientUuid ?? "unknown", $"CAPABILITIES_RECEIVED supports_components={payload.SupportsComponents}");

        await SendPacketAsync(PacketTypes.AUTH_OK, new AuthOkPayload());
        _authenticated = true;

        _logger.LogConnection(_clientUuid ?? "unknown", "AUTH_OK_SENT");
    }

    private void HandleChatMessage(ChatMessagePayload? payload)
    {
        if (payload == null || !_authenticated) return;

        _logger.LogChat(_clientUuid ?? "unknown", payload.Content);

        _ = EchoBackAsync(payload);
    }

    private async Task EchoBackAsync(ChatMessagePayload payload)
    {
        await SendPacketAsync(PacketTypes.CHAT_MESSAGE, payload);
    }

    private async Task HandlePingAsync(PingPayload? payload)
    {
        if (payload == null) return;

        _logger.LogConnection(_clientUuid ?? "unknown", $"PING timestamp={payload.TimestampMs}");

        await SendPacketAsync(PacketTypes.PONG, new PongPayload(payload.TimestampMs));
    }

    private void HandlePong(PongPayload? payload)
    {
        if (payload == null) return;

        var rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - payload.TimestampMs;
        _logger.LogConnection(_clientUuid ?? "unknown", $"PONG rtt={rtt}ms");
    }

    private async Task SendPacketAsync(int packetType, PacketPayload payload)
    {
        var packet = new MineChatPacket(packetType, payload);
        var serialized = packet.Serialize();
        var compressed = _compressionHandler.Compress(serialized);

        await _frameHandler.WriteFrameAsync(_sslStream, compressed, serialized.Length);
    }

    private void Close()
    {
        _running = false;
        if (_clientUuid != null)
        {
            _logger.LogConnection(_clientUuid, "DISCONNECTED");
        }
        _sslStream.Close();
        _client.Close();
    }
}
