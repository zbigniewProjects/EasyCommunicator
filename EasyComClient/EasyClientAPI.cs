﻿using LogSystem;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

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
    }

    public delegate void CommandBaseClient(string argsm, Response res);
    public delegate void ClientResponseBase(byte statusCode, string body);

    public enum ClientStatus {
        NotConnected,
        Connected,
    }

    public class EasyClientAPI
    {
        bool _initialized = false;

        public ClientStatus Status { get; internal set; } = ClientStatus.NotConnected;

        internal Client _client;
        internal IClientHandler _handler;

        public delegate void OnConnectedToServer();
        public OnConnectedToServer Callback_OnConnected;
        public OnConnectedToServer Callback_OnDisconnected;
        public OnConnectedToServer Callback_CouldNotConnect;

        internal CommandSystem CommandManager { private set; get; }

        ushort _seatID;

        IMessageConverter _messageConverter;

        public ILogger Logger;

        public Configuration Configuration = new Configuration();

        //must be method, not constructor, since this dll is also used in unity game engine
        public void Init()
        {
            /* unity may want to initialize stuff more then once, for example when users reopens editor window a lot
            so we have to keep track of how much it was initialized */
            if (_initialized) return;
            

            Logger = new Logger();
            Logger.SetLogLevel(LogLevel.None);

            _initialized = true;

            _messageConverter = new MessageConverter();

            _handler = new ClientHandler();
            _handler.Init(_messageConverter);

            _client = new Client();

            CommandManager = new CommandSystem();
            CommandManager.Init(Logger, _client, _handler);

            _client.Init(this, Logger, CommandManager, _messageConverter, _handler);
        }

        public void SetLogLevel(LogLevel logLevel)
        {
            if (Logger == null)
                throw new Exception("Initialize EasyClientAPI with .Init() method before setting log level");

            Logger.SetLogLevel(logLevel);
        }
        public void RegisterLogHandler(Action<string> logHandler) => Logger.RegisterWriter(logHandler);

        public void AssignSeatID(ushort seatID) => _seatID = seatID;

        public async Task<bool> Connect(string address, ushort port) => 
            await _client.ConnectToServer(_seatID, address, port);
        

        public void Disconnect()
        {
            _client.Disconnect();
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

        public async Task<Request> SendStructuredRequest<T>(string name, T msg) where T : struct
        {
            string serializedMsg = JsonConvert.SerializeObject(msg);
            return await _client.SendRequestToServer(name, serializedMsg);
        }
        public async Task<Request> SendRequest(string name, string body) =>
            await _client.SendRequestToServer(name, body);
        public async Task<Request> SendRequest(string name) =>
            await _client.SendRequestToServer(name, string.Empty);

        public void SendData<T>(T data) where T : struct
        {
            _client.SendMessage(data);
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