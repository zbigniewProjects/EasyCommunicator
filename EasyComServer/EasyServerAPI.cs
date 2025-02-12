//using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace EasyComServer
{
    public delegate void MessageHandler<T>(short clientID, T structData);
    public delegate void CommandBase(short clientID, string requestBody, Response response);

    public struct Request
    {
        public byte Code;
        public string Payload;

        public Request()
        {
            Code = 255;
            Payload = string.Empty;
        }
    }

    public struct Response 
    {
        ResponseBase _responseBase;
        public Response(ResponseBase response) 
        {
            _responseBase = response;
        }
        public void Respond(byte code, string payload) 
        {
            _responseBase.Invoke(code, payload);
        }
    }

    public class EasyServerAPI
    {
        public delegate void ClientConnectionEvent(short clientID);
        public delegate void ClientConnectedEvent(IClient client);
        public delegate void RequestToClientTimeout(short clientID, string reqName);
        public delegate void ClientDisconnectedEvent(short clientID, DisconnectCause disconnectCause);

        public ClientConnectedEvent Callback_OnClientConnected { get; set; }
        public ClientDisconnectedEvent Callback_OnClientDisconnected { get; set; }
        public ClientConnectionEvent Callback_OnUnexpectedClientTriedToConnect { get; set; }
        public ClientConnectionEvent Callback_OnDoubleConnectionDetected { get; set; }
        public RequestToClientTimeout Callback_OnRequestToClientTimeout { get; set; }

        public delegate void OnReceivedOldResponse(Request res);
        public OnReceivedOldResponse Callback_OnReceivedOutdatedResponse { get; set; }

        ServerHandler _handler;
        Server _server;
        CommandSystem _commandSystem;

        //api access to all connected clients, must be concurrent dictionary since this will be modified by multiple threads
        public ConcurrentDictionary<short, IClient> Clients => _server.Clients;

        IMessageConverter _messageConverter;

        public Configuration Configuration = new Configuration();

        public EasyServerAPI()
        {
            _messageConverter = new MessageConverter();
            _handler = new ServerHandler(_messageConverter);
            _commandSystem = new CommandSystem(this, _handler);
            _server = new Server(this);
        }

        [Obsolete]
        public void RegisterLogHandler(Action<string> logHandler) => throw new NotSupportedException();

        public void StartServer(ushort port, ushort maxConcurrentConnections)
        {
            Task.Run(()=>
                _server.Start(port, maxConcurrentConnections, _handler, _commandSystem, _messageConverter)
            );
        }
        public void StopServer()
        {
            _server.Stop();
        }

        public void RegisterEndpoint(string name, CommandBase method) => _commandSystem.RegisterCommand(name, method);

        public void DisconnectClient(short clientID)
        {
            _server.DisconnectClient(clientID);
        }

        public void SetUseSeatSystem(bool use) 
        {
            _server.UseSeatSystem = use;
        }
        public short RegisterSeatForConnection() => _server.RegisterClientSeat();
        
        public void RemoveSeatForConnection(short appID)
        {
            _server.RemoveClientSeat(appID);
        }

        public void RegisterMessageHandler<T>(MessageHandler<T> handler)
        {
            _handler.RegisterMessageHandler(handler);
        }
    }
    public class Configuration
    {
        /// <summary>
        /// Determines if EasyCommunicator should throw exception when trying to send REQUESTS while client is not connected to the server
        /// </summary>
        public bool ThrowException_WhenSendingRequestWhileNotConnected { get; set; } = true;

        /// <summary>
        /// Determines if EasyCommunicator should throw exception when trying to send DATA while client is not connected to the server
        /// </summary>
        public bool ThrowException_WhenSendingDataWhileNotConnected { get; set; } = true;

        /// <summary>
        /// How much time (miliseconds) must pass to stop waiting for server's response to out request 
        /// </summary>
        public int RequestTimeout { get; set; } = 10000;
    }
}
