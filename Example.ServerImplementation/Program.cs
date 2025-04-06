using EasyComServer;
public class Program
{
    static void Main(string[] args)
    {
        ushort port;
        try
        {
            port = Convert.ToUInt16(args[0]);
        }
        catch
        {
            port = 7777;
        }

        EasyServerAPI server = new EasyServerAPI();
        server.Configuration.ThrowException_WhenSendingDataWhileNotConnected = false;
        server.Configuration.ThrowException_WhenSendingRequestWhileNotConnected = false;

        if (!server.StartServer(port, 3)) 
            return;

        server.SetUseSeatSystem(false);

        Console.WriteLine($"Easy server listening on port: {port}");

        server.RegisterEndpoint("testreq", (short clientID, string requestBody, Response response) => {
            Console.WriteLine($"Received req from client #{clientID}: {requestBody}");
            response.Respond(0, "tak");
        });

        Random random = new Random();

        server.Callback_OnClientConnected += async (IClient client) => {
            Console.WriteLine($"Client {client.ID()} connected");
            for (int i = 0; i < 10; i++)
            {
                Request res = await client.SendRequest("req", random.Next(0, 1000).ToString());
                Console.WriteLine(res.Payload);
            }
        };

        server.Callback_OnClientDisconnected += (short clientID, DisconnectCause disconnectCause) => { Console.WriteLine($"Client {clientID} disconnect because: {disconnectCause}"); };

        Thread.Sleep(600);
        Console.ReadKey();
    }
}