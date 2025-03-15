using EasyComClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public class Program
{
    static async Task Main(string[] args)
    {
        ushort port;
        try
        {
            port = System.Convert.ToUInt16(args[0]);
        }
        catch 
        {
            port = 7777;
        }

        while (true)
        {
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < 1; i++)
            {
                tasks.Add(CreateAndClient());
            }
            await Task.WhenAll(tasks);
        }

        async Task CreateAndClient()
        {
            EasyClientAPI client = new EasyClientAPI();
            client.UserConfiguration.ThrowException_WhenSendingDataWhileNotConnected = false;
            client.UserConfiguration.ThrowException_WhenSendingRequestWhileNotConnected = false;

            Console.WriteLine($"Client connecting to {port}...");
            client.Callback_OnDisconnected += onDis;
            client.Callback_OnConnected += onCon;

            client.RegisterEndpoint("req", (string body, Response res) =>
            {
                Console.WriteLine($"Received reg from server {body}");
                res.Respond(0, "response from client");
            });

            await client.Connect("localhost", port);
            if (client.Status != ClientStatus.Connected)
                return;

            for (int i = 0; i < 100; i++)
            {
                if (client.Status != ClientStatus.Connected)
                    break;

                //Random random = new Random();
                /*Request req = await*/ client.SendRequest("testreq", $"request from client number {i}");
                //if (req.Code == 0)
                    //Console.WriteLine($"Received response from server {req.ResponseBody}");

                 //client.SendRequest("testreq", random.Next(0, 1000).ToString());
                Console.WriteLine($"Sent req {i}");
            }
            await Task.Delay(3000);
            client.Disconnect();
        }
    }

    private static void onCon()
    {
        Console.WriteLine("Connected to the server");
    }

    private static void onDis(DisconnectCause disconnectCause)
    {
        Console.WriteLine($"Disconnedted, cause: {disconnectCause}");
    }
}