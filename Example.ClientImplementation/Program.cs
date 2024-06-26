﻿using EasyComClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public class Program
{
    /* public async static Task Main(string[] args)
     {
         /*EasyClientAPI easyClient = new EasyClientAPI();
         easyClient.SetLogLevel(LogSystem.LogLevel.Information);

         //register callbacks for common events
         easyClient.Callback_OnConnected += () => Console.WriteLine("Connected to the server");
         easyClient.Callback_OnDisconnected += () => Console.WriteLine("Disconnected from the server");
         easyClient.Callback_CouldNotConnect += () => Console.WriteLine("Could not connect the server");

         //try connecting to server untill success
         while (easyClient.Status == ClientStatus.NotConnected)
         {
             await easyClient.Connect("localhost", 7777);
         }

         //connection successfull, now we can make requests to server
         while (true)
         {
             Console.WriteLine("Press key to request number number random from server");
             Console.ReadKey();

             //send request to easy communicator server component utylized by some parent app
             Request req = await easyClient.SendRequest("GenerateRandomNumber");

             //code 255 stands for request timeout, handle it here
             if (req.Code == 255)
             {
                 Console.WriteLine("Server did not respond on time");
                 continue;
             }

             //display number received from server
             Console.WriteLine($"Received random number: {req.ResponseBody}");
             Console.WriteLine();
         }
     }*/
    static async Task Main(string[] args)
    {
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
            client.Configuration.ThrowException_WhenSendingDataWhileNotConnected = false;
            client.Configuration.ThrowException_WhenSendingRequestWhileNotConnected = false;

            Console.WriteLine("Client connecting...");
            client.Callback_OnDisconnected += onDis;
            client.Callback_OnConnected += onCon;

            client.RegisterEndpoint("req", (string body, Response res) =>
            {
                Console.WriteLine($"Received reg from server {body}");
                res.Respond(0, "");
            });

            await client.Connect("localhost", 7777);
            if (client.Status != ClientStatus.Connected)
                return;

            //for (int i = 0; i < 10; i++)
            //{
            //    if (client.Status != ClientStatus.Connected)
            //        break;

            //    Random random = new Random();
            //    Request req = await client.SendRequest("testreq", "body");
            //    if (req.Code == 0)
            //        Console.WriteLine($"Received response from server {req.ResponseBody}");
            //    await client.SendRequest("testreq", random.Next(0, 1000).ToString());
            //    if (req.Code == 0)
            //        Console.WriteLine($"Received response from server {req.ResponseBody}");
            //}
            await Task.Delay(6000);
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