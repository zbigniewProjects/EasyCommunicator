using EasyComClient;
public class Program
{
    public async static Task Main(string[] args)
    {
        EasyClientAPI easyClient = new EasyClientAPI();
        easyClient.Init();
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

            //send request to easy communicator server component
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
    }
}