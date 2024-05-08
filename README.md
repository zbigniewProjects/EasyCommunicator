### EasyCommunicator
Utility for .NET applications that facilitates bidirectional communication between client and server, akin to HTTP but with support for both client-to-server and server-to-client interactions.
 - [Website](https://zbigniew.dev/projects/easycommunicator)

## Getting Started

To use these utilities in your .NET projects:

1. Clone this repository
2. Build EasyComServer and EasyComClient projects to dll's
3. Reference the compiled DLLs in your own projects accordingly.

## Usage

Example EasyComServer implementation. Code below:
1. Creates server class
2. Registers endpoint "GenerateRandomNumber" on server that client will be able to send request to
3. Starts server on port localhost:7777
```csharp
using EasyComServer; 
public class Program
{
    static EasyServerAPI _easyServer;
    public static void Main(string[] args)
    {
        //First create EasyCommunicator server instance
        _easyServer = new EasyServerAPI();
       
        //we can register callbacks for common events such as clients connecting and disconnecting
        _easyServer.Callback_OnClientConnected += (IClient client) => Console.WriteLine($"Client #{client.ID()} connected.");
        _easyServer.Callback_OnClientDisconnected += (short clientID) => Console.WriteLine($"Client #{clientID} disconnected.");

        //register endpoint on which client will issue requests
        _easyServer.RegisterEndpoint("GenerateRandomNumber", (short clientID, string requestBody, Response response) => {
            Console.WriteLine($"Client number {clientID} requested random number!");

            //generate random number
            Random random = new Random();
            int randomNumber = random.Next(0, 1000);

            //respond to client with random number as requested. first value is response code (0), and second is body (random number)
            response.Respond(0, randomNumber.ToString());
            Console.WriteLine($"Random number {randomNumber} sent to client #{clientID}.");
        });

        //after registering endpoints we can start the server
        ushort port = 7777;
        ushort maxConcurrentClients = 100;
        Console.WriteLine($"Started server on port {port}");
        _easyServer.StartServer(port, maxConcurrentClients);

        //prevent app from immediate closure
        Console.ReadKey();
    }
}
```
Example EasyComClient implementation. Code below:
1. Creates client class
2. Connect client to the server that is listening on localhost:5000
3. Sends request to server on endpoint "GenerateRandomNumber" on every key press
4. Prints received number to the console
```csharp
using EasyComClient;
public class Program
{
    public async static Task Main(string[] args)
    {
        EasyClientAPI easyClient = new EasyClientAPI();
        easyClient.Init();

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

            //send request to easy communicator server component utilized by some parent app
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
```
