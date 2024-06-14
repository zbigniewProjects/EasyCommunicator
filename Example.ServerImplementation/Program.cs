using EasyComServer;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
public class Program
{
    static void Main(string[] args)
    {
        EasyServerAPI server = new EasyServerAPI(new Logger<Program>(new LoggerFactory()));
        server.Configuration.ThrowException_WhenSendingDataWhileNotConnected = false;
        server.Configuration.ThrowException_WhenSendingRequestWhileNotConnected = false;
        server.StartServer(7777, 20);
        server.SetUseSeatSystem(false);

        Console.WriteLine($"Easy server started on port {7777}");

        server.RegisterEndpoint("testreq", (short clientID, string requestBody, Response response) => {
            Console.WriteLine($"Received req from client #{clientID}: {requestBody}");
            response.Respond(0, "tak");
        });

        Random random = new Random();

        server.Callback_OnClientConnected += (IClient client) => {
            Console.WriteLine($"Client {client.ID()} connected");
            for (int i = 0; i < 10; i++)
            {
                client.SendRequest("req", random.Next(0, 1000).ToString());
            }
        };

        server.Callback_OnClientDisconnected += (short clientID, DisconnectCause disconnectCause) => { Console.WriteLine($"Client {clientID} disconnect because: {disconnectCause}"); };

        Thread.Sleep(1000000000);
        Console.ReadKey();
    }
}