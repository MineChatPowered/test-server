using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Minechat.Server.Connection;
using Minechat.Server.Logging;

namespace Minechat.Server;

class Program
{
    private const int DefaultPort = 7632;
    private const string CertFile = "server.pfx";
    private const string CertPassword = "minechat";

    static async Task Main(string[] args)
    {
        var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : DefaultPort;
        var logger = new ChatLogger("chat.log");

        Console.WriteLine($"MineChat Echo Test Server v1.0.0");
        Console.WriteLine($"Listening on TCP {port} with TLS...");

        var serverCert = GetOrCreateCertificate();

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        Console.WriteLine($"Server started on port {port}");
        Console.WriteLine("Press Ctrl+C to stop");

        while (true)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync();
                Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

                var connection = new ClientConnection(client, serverCert, logger);
                _ = connection.RunAsync();
            }
            catch (Exception ex)
            {
                logger.LogError("Accept error", ex);
            }
        }
    }

    static X509Certificate2 GetOrCreateCertificate()
    {
        if (File.Exists(CertFile))
        {
            return new X509Certificate2(CertFile, CertPassword);
        }

        Console.WriteLine("Generating self-signed certificate...");

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

        Console.WriteLine($"Certificate saved to {CertFile}");

        return new X509Certificate2(CertFile, CertPassword);
    }
}
