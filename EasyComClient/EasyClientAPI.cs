//using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using static EasyComClient.Client;

namespace EasyComClient
{
    public struct Request
    {
        public byte Code;
        public string ResponseBody;
    }

    public struct Response
    {
        ClientResponseBase _responseCallback;
        internal Response(ClientResponseBase clientResponseCallback) => _responseCallback = clientResponseCallback;
        public void Respond(byte code, string payload) => _responseCallback.Invoke(code, payload);
        public void Respond(byte code) => _responseCallback.Invoke(code, string.Empty);
    }

    public delegate void CommandBaseClient(string argsm, Response res);
    public delegate void ClientResponseBase(byte statusCode, string body);

    public enum ClientStatus {
        NotConnected,
        Connected,
    }

    public class EasyClientAPI
    {
        public ClientStatus Status { get; internal set; } = ClientStatus.NotConnected;

        internal Client Client;
        internal IClientHandler _handler;

        public delegate void OnConnectedToServer();
        public delegate void OnDisconnectedFromServer(DisconnectCause disconnectCause);
        public OnConnectedToServer Callback_OnConnected;
        public OnDisconnectedFromServer Callback_OnDisconnected;
        public OnConnectedToServer Callback_CouldNotConnect;

        internal CommandSystem CommandManager { private set; get; }

        short _seatID;

        IMessageConverter _messageConverter;
       // public ILogger Logger;

        public Configuration Configuration = new Configuration();

        //must be method, not constructor, since this dll is also used in unity game engine
        public EasyClientAPI()
        {
            //Logger = logger;

            _messageConverter = new MessageConverter();
            _handler = new ClientHandler(_messageConverter);
            CommandManager = new CommandSystem(this, _handler);
            Client = new Client(this, CommandManager, _messageConverter, _handler);
        }

        public void AssignSeatID(short seatID) => _seatID = seatID;

        public async Task<bool> Connect(string address, ushort port) => 
            await Client.ConnectToServer(_seatID, address, port);
        

        public void Disconnect()
        {
            Client.Disconnect(DisconnectCause.Disconnected);
        }

        public delegate void MessageHandler<T>(T structData);

        public void RegisterMessageHandler<T>(MessageHandler<T> handler)
        {
            _handler.RegisterMessageHandler<T>(handler);
        }

        public void RegisterEndpoint(string name, CommandBaseClient method)
        {
            CommandManager.RegisterCommand(name, method);
        }

        /*public async Task<Request> SendStructuredRequest<T>(string name, T msg) where T : struct
        {
            string serializedMsg = JsonConvert.SerializeObject(msg);
            return await Client.SendRequestToServer(name, serializedMsg);
        }*/
        public async Task<Request> SendRequest(string name, string body) =>
            await Client.SendRequestToServer(name, body);
        public async Task<Request> SendRequest(string name) =>
            await Client.SendRequestToServer(name, string.Empty);

        public void SendData<T>(T data) where T : struct
        {
            Client.SendMessage(data);
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

        /// <summary>
        /// Timeout (in miliseconds) for establishing connection with server
        /// </summary>
        public int ConnectionTimeout { get; set; } = 5000;
    }
}
