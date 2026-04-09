using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Minechat.Server.Compression;
using Minechat.Server.Framing;
using Minechat.Server.Logging;
using Minechat.Server.Protocols;
using Serilog;

namespace Minechat.Server.Connection;

public class ClientConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly SslStream _sslStream;
    private readonly string _connectionId;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly FrameHandler _frameHandler;
    private readonly ICompressionHandler _compressionHandler;
    private readonly IChatLogger _chatLogger;
    private readonly Timer _pingTimer;

    public string ConnectionId => _connectionId;
    public bool IsAuthenticated => _authenticated;

    private bool _running = true;
    private string? _clientUuid;
    private string? _minecraftUuid;
    private bool _authenticated;
    private long _lastPacketTime;
    private string[]? _clientSupportedFormats;
    private string? _clientPreferredFormat;
    private bool _muted;
    private readonly Action<ChatMessagePayload, string?>? _broadcastCallback;

    public string[]? SupportedFormats => _clientSupportedFormats;
    public string? PreferredFormat => _clientPreferredFormat;

    public ClientConnection(TcpClient client, X509Certificate2 serverCert, string connectionId,
        CancellationToken cancellationToken, TimeSpan keepAliveTimeout, int pingIntervalSeconds,
        int connectionTimeoutSeconds, IChatLogger chatLogger, Action<ChatMessagePayload, string?>? broadcastCallback = null)
    {
        _client = client;
        _connectionId = connectionId;
        _cancellationToken = cancellationToken;
        _keepAliveTimeout = keepAliveTimeout;
        _lastPacketTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _chatLogger = chatLogger;
        _broadcastCallback = broadcastCallback;

        _sslStream = new SslStream(
            _client.GetStream(),
            false,
            (_, _, _, _) => true
        );

        _sslStream.ReadTimeout = connectionTimeoutSeconds * 1000;

        _frameHandler = new FrameHandler();
        _compressionHandler = new CliCompressor();

        _pingTimer = new Timer(SendPing, null, TimeSpan.FromSeconds(pingIntervalSeconds), TimeSpan.FromSeconds(pingIntervalSeconds));

        try
        {
            _sslStream.AuthenticateAsServer(serverCert);
            Log.Debug("[{ConnectionId}] TLS handshake successful", _connectionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{ConnectionId}] TLS handshake failed", _connectionId);
            throw;
        }
    }

    public async Task RunAsync()
    {
        try
        {
            while (_running && !_cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now - _lastPacketTime > _keepAliveTimeout.TotalMilliseconds)
                {
                    Log.Warning("[{ConnectionId}] Connection timed out after {Timeout}ms of inactivity",
                        _connectionId, _keepAliveTimeout.TotalMilliseconds);
                    break;
                }

                var frame = await _frameHandler.ReadFrameAsync(_sslStream, _cancellationToken);
                if (frame == null)
                    break;

                _lastPacketTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var (decompressedSize, _, compressedData) = frame.Value;

                var decompressed = _compressionHandler.Decompress(compressedData, decompressedSize);
                var packet = MineChatPacket.Deserialize(decompressed);

                if (packet == null)
                {
                    Log.Error("[{ConnectionId}] Failed to deserialize packet. Data hex: {Hex}",
                        _connectionId, Convert.ToHexString(decompressed.Take(64).ToArray()));
                    break;
                }

                Log.Debug("[{ConnectionId}] Successfully deserialized packet type {PacketType}",
                    _connectionId, packet.PacketType);

                await HandlePacketAsync(packet);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("[{ConnectionId}] Connection cancelled", _connectionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{ConnectionId}] Connection error", _connectionId);
        }
        finally
        {
            Close();
        }
    }

    private void SendPing(object? state)
    {
        if (!_running || _cancellationToken.IsCancellationRequested)
            return;

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var payload = new PingPayload(timestamp);
            _ = SendPacketAsync(PacketTypes.PING, payload);
            Log.Debug("[{ConnectionId}] Sent PING with timestamp {Timestamp}", _connectionId, timestamp);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{ConnectionId}] Failed to send PING", _connectionId);
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
            case PacketTypes.MODERATION:
                HandleModeration(packet.Payload as ModerationPayload);
                break;
        }
    }

    private async Task HandleLinkAsync(LinkPayload? payload)
    {
        if (payload == null) return;

        _clientUuid = payload.ClientUuid;
        var linkingCode = payload.LinkingCode;

        _minecraftUuid = Guid.NewGuid().ToString();

        Log.Information("[{ConnectionId}] LINK_RECEIVED code={LinkingCode}, assigned_minecraft_uuid={MinecraftUuid}",
            _connectionId, linkingCode, _minecraftUuid);

        var responsePayload = new LinkOkPayload(_minecraftUuid);
        await SendPacketAsync(PacketTypes.LINK_OK, responsePayload);

        Log.Information("[{ConnectionId}] LINK_OK_SENT", _connectionId);
    }

    private async Task HandleCapabilitiesAsync(CapabilitiesPayload? payload)
    {
        if (payload == null) return;

        _clientSupportedFormats = payload.SupportedFormats;
        _clientPreferredFormat = payload.PreferredFormat;

        Log.Information("[{ConnectionId}] CAPABILITIES_RECEIVED supported_formats=[{SupportedFormats}], preferred_format={PreferredFormat}",
            _connectionId, string.Join(",", payload.SupportedFormats), payload.PreferredFormat);

        await SendPacketAsync(PacketTypes.AUTH_OK, new AuthOkPayload());
        _authenticated = true;

        Log.Information("[{ConnectionId}] AUTH_OK_SENT", _connectionId);
    }

    private void HandleChatMessage(ChatMessagePayload? payload)
    {
        if (payload == null || !_authenticated) return;

        if (_muted)
        {
            Log.Warning("[{ConnectionId}] Message rejected: client is muted", _connectionId);
            return;
        }

        var sender = _minecraftUuid ?? _clientUuid ?? "unknown";
        _chatLogger.LogChat(sender, payload.Content);

        Log.Information("[{ConnectionId}] CHAT_MESSAGE from={Sender}: {Content}", _connectionId, sender, payload.Content);

        if (_broadcastCallback != null)
        {
            _broadcastCallback(payload, _connectionId);
        }
    }

    private async Task HandlePingAsync(PingPayload? payload)
    {
        if (payload == null) return;

        Log.Debug("[{ConnectionId}] Received PING with timestamp {Timestamp}", _connectionId, payload.TimestampMs);

        await SendPacketAsync(PacketTypes.PONG, new PongPayload(payload.TimestampMs));

        Log.Information("[{ConnectionId}] Responded to PING with timestamp {Timestamp}", _connectionId, payload.TimestampMs);
    }

    private void HandlePong(PongPayload? payload)
    {
        if (payload == null) return;

        var rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - payload.TimestampMs;
        Log.Information("[{ConnectionId}] Received PONG rtt={Rtt}ms", _connectionId, rtt);
    }

    private void HandleModeration(ModerationPayload? payload)
    {
        if (payload == null) return;

        Log.Information("[{ConnectionId}] MODERATION action={Action}, scope={Scope}, reason={Reason}",
            _connectionId, payload.Action, payload.Scope, payload.Reason);

        switch (payload.Action)
        {
            case ModerationAction.WARN:
                Log.Warning("[{ConnectionId}] Client warned: {Reason}", _connectionId, payload.Reason);
                break;
            case ModerationAction.MUTE:
                _muted = true;
                Log.Information("[{ConnectionId}] Client muted", _connectionId);
                break;
            case ModerationAction.KICK:
                SendSystemDisconnect(SystemDisconnectReason.SHUTDOWN, payload.Reason ?? "Kicked");
                Close();
                break;
            case ModerationAction.BAN:
                SendSystemDisconnect(SystemDisconnectReason.SHUTDOWN, payload.Reason ?? "Banned");
                Close();
                break;
        }
    }

    private async Task SendPacketAsync(int packetType, PacketPayload payload)
    {
        var packet = new MineChatPacket(packetType, payload);
        var serialized = packet.Serialize();
        var compressed = _compressionHandler.Compress(serialized);

        await _frameHandler.WriteFrameAsync(_sslStream, compressed, serialized.Length, _cancellationToken);
    }

    public void SendPacket(int packetType, PacketPayload payload)
    {
        _ = SendPacketAsync(packetType, payload);
    }

    public void SendSystemDisconnect(int reasonCode, string message)
    {
        var payload = new SystemDisconnectPayload(reasonCode, message);
        _ = SendPacketAsync(PacketTypes.SYSTEM_DISCONNECT, payload);
    }

    public void Close()
    {
        if (_running && _clientUuid != null)
        {
            Log.Information("[{ConnectionId}] DISCONNECTED client={ClientUuid}, minecraft_uuid={MinecraftUuid}",
                _connectionId, _clientUuid, _minecraftUuid);
        }
        _running = false;
        Dispose();
    }

    public void Dispose()
    {
        _pingTimer.Dispose();

        try
        {
            _sslStream.Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[{ConnectionId}] Error closing SSL stream", _connectionId);
        }

        try
        {
            _client.Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[{ConnectionId}] Error closing TCP client", _connectionId);
        }
    }
}
