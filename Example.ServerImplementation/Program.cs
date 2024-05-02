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