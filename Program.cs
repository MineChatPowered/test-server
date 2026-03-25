using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Minechat.Server.Connection;
using Minechat.Server.Logging;
using Serilog;
using Serilog.Events;

namespace Minechat.Server;

class Program
{
    private const string CertFile = "server.pfx";
    private const string CertPassword = "minechat";

    private static readonly ConcurrentBag<ClientConnection> _connections = new();
    private static CancellationTokenSource? _cts;

    static async Task Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : ServerConfig.DEFAULT_PORT;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                ServerConfig.LOG_FILE_PATH,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("MineChat Echo Test Server v1.0.0");

        var chatLogger = new ChatLogger();
        _cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Log.Information("Shutdown signal received, closing connections...");
            _cts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            Log.Information("Process exiting, closing all connections...");
            foreach (var conn in _connections)
            {
                conn.Close();
            }
            Log.CloseAndFlush();
        };

        var serverCert = GetOrCreateCertificate();

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        Log.Information("Listening on TCP {Port} with TLS...", port);
        Log.Information("Press Ctrl+C to stop");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(_cts.Token);

                    if (_connections.Count >= ServerConfig.MAX_CLIENTS)
                    {
                        Log.Warning("Max clients {MaxClients} reached, rejecting new connection from {RemoteEndPoint}",
                            ServerConfig.MAX_CLIENTS, client.Client.RemoteEndPoint);
                        client.Close();
                        continue;
                    }

                    Log.Information("Client connected: {RemoteEndPoint}", client.Client.RemoteEndPoint);

                    var connectionId = Guid.NewGuid().ToString("N")[..8];
                    var connection = new ClientConnection(client, serverCert, connectionId, _cts.Token,
                        TimeSpan.FromSeconds(ServerConfig.KEEP_ALIVE_TIMEOUT_SECONDS),
                        ServerConfig.PING_INTERVAL_SECONDS,
                        ServerConfig.CONNECTION_TIMEOUT_SECONDS,
                        chatLogger);

                    _connections.Add(connection);
                    _ = connection.RunAsync().ContinueWith(t =>
                    {
                        _connections.TryTake(out var _);
                        if (t.IsFaulted)
                        {
                            Log.Error(t.Exception, "Connection {ConnectionId} failed", connectionId);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Accept error");
                }
            }
        }
        finally
        {
            Log.Information("Server shutting down...");
            foreach (var conn in _connections)
            {
                conn.Close();
            }
            listener.Stop();
            Log.CloseAndFlush();
        }
    }

    static X509Certificate2 GetOrCreateCertificate()
    {
        if (File.Exists(CertFile))
        {
            return new X509Certificate2(CertFile, CertPassword);
        }

        Log.Information("Generating self-signed certificate...");

        var distinguishedName = new X500DistinguishedName("CN=localhost");
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true
            )
        );
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false
            )
        );

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1)
        );

        var pfxBytes = certificate.Export(X509ContentType.Pfx, CertPassword);
        File.WriteAllBytes(CertFile, pfxBytes);

        Log.Information("Certificate saved to {CertFile}", CertFile);

        return new X509Certificate2(CertFile, CertPassword);
    }
}