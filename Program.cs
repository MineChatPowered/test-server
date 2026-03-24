using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Minechat.Server;

class Program
{
    static async Task Main(string[] args)
    {
        var server = new TcpListener(IPAddress.Any, 7632);
        server.Start();
        Console.WriteLine("Minechat.Server listening on TCP 7632");

        while (true)
        {
            var client = await server.AcceptTcpClientAsync();
            _ = HandleClient(client);
        }
    }

    static async Task HandleClient(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer);
            var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Received: {message}");

            var response = "Welcome to Minechat.Server!\n";
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes);
        }
    }
}
