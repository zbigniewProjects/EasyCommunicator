using System.Net.Sockets;
using System.Net;
using System.Collections.Concurrent;
//using Microsoft.Extensions.Logging;

namespace EasyComServer
{
    class Server
    {
        //ILogger _logger;

        EasyServerAPI _dnComInterface;

        internal IServerHandler Handler {  set; get; }
        internal CommandSystem CommandManager {  set; get; }
        public bool UseSeatSystem { get; internal set; } = false;
        internal ConcurrentDictionary<short, IClient> Clients = new ConcurrentDictionary<short, IClient>();

        public ushort Port = ushort.MaxValue;

        private TcpListener _tcpListener;

        internal ConcurrentBag<short> WaitingSeats = new ConcurrentBag<short>();

        public ConcurrentQueue<short> _freeClientIDs;
        ushort _maxConcurrentConnections;

        IMessageConverter _messageConverter;

        CancellationTokenSource _serverCancellationToken;

        ServerState _currentServerState;
        private Task _handleClientsTask;

        enum ServerState 
        {
            Off,
            Starting,
            Closing,
            Listening,
        }

        public Server(EasyServerAPI dNCommunicatorAPI/*, ILogger logger*/)
        {
            _dnComInterface = dNCommunicatorAPI;
            //_logger = logger;
        }

        public async void Start(ushort port, ushort maxConnections, IServerHandler handler, CommandSystem cmdManager, IMessageConverter messageConverter)
        {
            ushort maxLimit = 32000;
            if (maxConnections > maxLimit) 
            {
                //_dnComInterface.Logger.LogWarning($"Cannot set more concurrent connections to more than {maxLimit}, using {maxLimit} instead.");
                maxConnections = maxLimit;
            }

            _messageConverter = messageConverter;
            _freeClientIDs = new ConcurrentQueue<short>();
            _maxConcurrentConnections = maxConnections;

            //from this Queue we will be taking id's that are not assigned to any, to assign them to new clients
            //when client disconnects, we will enqueue his id back here, this way we will have always one unique id for every connected client
            for (short i = 0; i < maxConnections; i++)
            {
                _freeClientIDs.Enqueue(i);
            }

            if (_currentServerState == ServerState.Starting) 
            {
                //_dnComInterface.Logger.LogWarning($"Server on port {port} is already starting...");
                return;
            }

            if (_currentServerState == ServerState.Closing) 
            {
                //_dnComInterface.Logger.LogWarning($"Server on port {port} is closing, cant start it now");
                return;
            }

            if (_currentServerState == ServerState.Listening) 
            {
                //_dnComInterface.Logger.LogWarning($"Server on port {port} is already running. Do not start same server instance twice.");
                return;
            }
            _currentServerState = ServerState.Starting;

            Handler = handler; 
            CommandManager = cmdManager;

            Port = port;

            IPAddress iPAddress = IPAddress.Any;
            TcpListener tcpListener = new TcpListener(iPAddress, port);
            try
            {
                tcpListener.Start();
            }
            catch 
            {
                //_dnComInterface.Logger.LogWarning($"Port {port} is already in use, could not start server");
                _currentServerState = ServerState.Off;
                return;
            }
            
            _currentServerState = ServerState.Listening;
            _handleClientsTask = Task.Run(HandleClients);

            //_dnComInterface.Logger.LogInformation($"Server started on {iPAddress}:{port}");
            _tcpListener = tcpListener;

            _serverCancellationToken = new CancellationTokenSource();

            while (_currentServerState == ServerState.Listening || _currentServerState == ServerState.Closing)
            {
                try
                {
                    //Console.Wri
                    TcpClient newClient = await _tcpListener.AcceptTcpClientAsync(_serverCancellationToken.Token);
                    if (_freeClientIDs.Count == 0)
                    {
                       // _logger.LogWarning($"New client client tried to connect but maximum amount of concurrent connections ({_maxConcurrentConnections}) is reached, disconnecting");
                        newClient.Close();
                        newClient.Dispose();
                        continue;
                    }
                    IClient client = new Client(_dnComInterface, Handler, _messageConverter);
                    client.Connect(newClient, this);
                }
                catch (OperationCanceledException) 
                {
                    IClient[] clients = Clients.Values.ToArray();
                    for (int i = 0; i < clients.Length; i++)
                    {
                      //  _logger.LogInformation($"Cancelling client {clients[i].ID()} due to server closure");
                        clients[i].Disconnect(DisconnectCause.Disconnected);
                        foreach (IClient client in Clients.Values)
                        {
                            client.HandleStream();
                        }
                    }

                    
                    _currentServerState = ServerState.Off;

                    _tcpListener.Stop();
                    _tcpListener.Dispose();
                    _tcpListener = null;

                    _freeClientIDs.Clear();
                    WaitingSeats.Clear();

                    _maxConcurrentConnections = 0;

                 //   _logger.LogInformation($"Server from port {Port} stopped");
                }
            }
        }

        async Task HandleClients()
        {
            while (true)
            {
                if (_currentServerState == ServerState.Closing)
                {
                    _serverCancellationToken.Cancel();
                    return;
                }

                foreach (IClient client in Clients.Values)
                {
                    client.HandleStream();
                }

                await Task.Delay(50);
            }
        }

        public void Stop() 
        {
            if (_currentServerState == ServerState.Listening)
            {
                _currentServerState = ServerState.Closing;
            }
        }

        public void DisconnectClient(short id) 
        {
            if(Clients.TryGetValue(id, out IClient client))
                client.Disconnect(DisconnectCause.Disconnected);
        }

        public void OnDisconnectClient(short clientID, DisconnectCause disconnectCause) 
        {
            _dnComInterface.Callback_OnClientDisconnected?.Invoke(clientID, disconnectCause);
            Clients.TryRemove(clientID, out IClient client);
            _freeClientIDs.Enqueue(clientID);
        }

        public short RegisterClientSeat()
        {
            if (_freeClientIDs.TryDequeue(out short id))
            {
                WaitingSeats.Add(id);
                return id;
            }
            else return -1;
        }
        public bool RemoveClientSeat(short seat) => WaitingSeats.TryTake(out short seatt);
        public bool DoesSeatExist(short appID) => WaitingSeats.Contains(appID);

        internal short GetAndDequeueFreeClientID()
        {
            if (_freeClientIDs.TryDequeue(out short id))
                return id;
            else return -1;
        }
    }
}
