using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LogSystem;

namespace EasyComClient
{    
    internal class Client
    {
        public enum ConnectionStatus 
        {
            NotConnected,
            Connecting,
            Connected,
            Disconnecting,
        }

        public ConnectionStatus ClientStatus = ConnectionStatus.NotConnected;

        public TCP Tcp;
        public ushort SeatHash;
        
        IMessageConverter _messageConverter;

        public EasyClientAPI EasyClientAPI;
        CommandSystem _commandSystem;

        internal delegate void ReceivedResponseCallback(Request res);
        internal ConcurrentDictionary<ushort, ReceivedResponseCallback> _pendingRequests { get; set; } = new ConcurrentDictionary<ushort, ReceivedResponseCallback>();
        ushort _requestCounter;

        internal void Init(EasyClientAPI api, ILogger logger, CommandSystem commandSystem, IMessageConverter messageConverter, IClientHandler handler)
        {
            if (Tcp != null) return;

            EasyClientAPI = api;
            _messageConverter = messageConverter;
            _commandSystem = commandSystem;

            Tcp = new TCP(api, this, logger, handler);
        }

        public async Task<bool> ConnectToServer(ushort seatHash, string serverAddress, ushort port)
        {
            SeatHash = seatHash;
            return await Tcp.Connect(seatHash, serverAddress, port);
        }

        public void Disconnect() 
        {
            //set connection status to disconnecting so thread that processes network stream can detect it and safely handle disconnection
            ClientStatus = ConnectionStatus.Disconnecting;
        }

        public async void HandleConnection() 
        {
            while (true)
            {
                if (ClientStatus == ConnectionStatus.Disconnecting)
                {
                    //timeout all pending request before disconnecting
                    EasyClientAPI.Logger.LogWarning($"Relasing all pending requests: {_pendingRequests.Count}");
                    foreach (ReceivedResponseCallback item in _pendingRequests.Values)
                    {
                        item.Invoke(new Request { Code = 254 });
                    }

                    Tcp.Disconnect(0);
                    EasyClientAPI.Status = EasyComClient.ClientStatus.NotConnected;
                    return;
                }
                
                Tcp.HandleConnectionRead();
                Tcp.HandleConnectionWrite();
                await Task.Delay(50);
            }
        }

        public async Task<Request> SendRequestToServer(string name, string data)
        {
            
            if (EasyClientAPI.Configuration.ThrowException_WhenSendingRequestWhileNotConnected &&
                ClientStatus != ConnectionStatus.Connected)
                throw new Exception("Cannot send command when client is not connected to the server");

            Request res = new Request();
            res.Code = byte.MaxValue; //code 255 means request timeout. This wont be overriden if we don't receive response, so timeout

            if (ClientStatus != ConnectionStatus.Connected) return res;

            if (_pendingRequests.Count >= ushort.MaxValue)
            {
                EasyClientAPI.Logger.LogWarning($"Max amaount of awaiting requests reached ({ushort.MaxValue})");
                return res;
            }

            object locker = new object();
            ushort reqID;

            ManualResetEventSlim callbackEvent = new ManualResetEventSlim();

            lock (_pendingRequests)
            {
                reqID = _requestCounter;

                _pendingRequests.TryAdd(reqID, ReceivedResponseCallback);

                _requestCounter++;
                if (_requestCounter == ushort.MaxValue)
                    _requestCounter = 0;

                EasyClientAPI.Logger.LogWarning($"Registering requests {_pendingRequests.Count}");
            }
            //send message to client
            SendMessage(new CommandMsg
            {
                CmdHash = _commandSystem.GetExistingPostCmdHash(name),
                Payload = data,
                ReqID = reqID
            });

            await Task.Run(() =>
            {
                callbackEvent.Wait(EasyClientAPI.Configuration.RequestTimeout);
                callbackEvent.Dispose();
            });

            lock (_pendingRequests){
                _pendingRequests.TryRemove(reqID, out ReceivedResponseCallback value);
            }
            return res;

            void ReceivedResponseCallback(Request _res)
            {
                res = _res;
                callbackEvent.Set();
            }
        }

        public void SendMessage<T>(T msg) where T : struct
        {
            if (ClientStatus != ConnectionStatus.Connected && EasyClientAPI.Configuration.ThrowException_WhenSendingDataWhileNotConnected)
                throw new Exception("Can not send data while client is not connected");

            Tcp.SendData(_messageConverter.SerializeWithMsgID(msg));
        }


        public class TCP
        {
            ILogger _logger;

            public TcpClient socket;
            NetworkStream _networkStream;
            private byte[] _receiveBuffer;

            bool _isConnected;

            ushort AppID;
            IClientHandler _handler;

            public static int DataBufferSize = 1024 * 1024;

            ConcurrentQueue<byte[]> _dataToSend = new ConcurrentQueue<byte[]>();

            int _currentMessageLength = 0;
            int _currentDataLength = 0;

            Client _client;
            EasyClientAPI _easyClientAPI;

            public TCP(EasyClientAPI easyClientAPI, Client client, ILogger logger, IClientHandler handler)
            {
                _handler = handler;
                _logger = logger;
                _client = client;
                _easyClientAPI = easyClientAPI;
            }
            public async Task<bool> Connect(ushort appID, string serverAddress, ushort port)
            {
                if (_client.ClientStatus == ConnectionStatus.Connecting)
                {
                    _logger.LogWarning("Already connecting...");
                    return false;
                }
                if (_client.ClientStatus == ConnectionStatus.Disconnecting)
                {
                    _logger.LogWarning("Already disconnecting...");
                    return false;
                }

                if (_client.ClientStatus == ConnectionStatus.Connected)
                {
                    _logger.LogWarning("Tried to connect to server when connection is already established");
                    return false;
                }

                AppID = appID;

                socket = new TcpClient
                {
                    ReceiveBufferSize = DataBufferSize,
                    SendBufferSize = DataBufferSize,
                };
                socket.ReceiveTimeout = 50;
                socket.SendTimeout = 50;
                _receiveBuffer = new byte[DataBufferSize * 2];

                using (var timeoutCancellationTokenSource = new CancellationTokenSource())
                {
                    await Task.WhenAny(
                        socket.ConnectAsync(serverAddress, port),
                        Task.Delay(_easyClientAPI.Configuration.ConnectionTimeout,
                        timeoutCancellationTokenSource.Token));

                    if (socket != null && socket.Connected)
                    {
                        _isConnected = true;

                        _networkStream = socket.GetStream();
                        try
                        {
                            //send server our identifier so server will be able to connect us to its services and states
                            _networkStream.Write(BitConverter.GetBytes(AppID), 0, 2);
                        }
                        catch
                        {
                            ConnectionFailed();
                            return false;
                        }


                        _client.EasyClientAPI.Callback_OnConnected?.Invoke();
                        _client.ClientStatus = ConnectionStatus.Connected;
                        _client.HandleConnection();
                        _logger.LogWarning("Connected to the server");
                        _easyClientAPI.Status = EasyComClient.ClientStatus.Connected;
                        return true;
                    }
                    else
                    {
                        timeoutCancellationTokenSource.Cancel();
                        ConnectionFailed();
                        return false;
                    }

                    void ConnectionFailed()
                    {
                        _client.ClientStatus = ConnectionStatus.NotConnected;
                        _isConnected = false;
                        if (socket != null)
                        {
                            socket.Close();
                            socket.Dispose();
                            socket = null;
                        }
                        if (_networkStream != null)
                        {
                            _networkStream.Close();
                            _networkStream.Dispose();
                            _networkStream = null;
                        }
                        _client.EasyClientAPI.Callback_CouldNotConnect?.Invoke();
                    }
                }
            }

            public void HandleConnectionRead()
            {
                int streamLength = DataBufferSize;

                while (streamLength == DataBufferSize)
                {
                    if (!_isConnected) return;

                    try
                    {
                        streamLength = _networkStream.Read(_receiveBuffer, _currentDataLength, DataBufferSize);
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException socketEx)
                    {
                        if (socketEx.SocketErrorCode == SocketError.TimedOut)
                        {
                            // Handle write timeout exception
                            return;
                        }
                        else if (socketEx.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            // Handle remote host closure
                            _client.Disconnect();
                            return;
                        }
                        else
                        {
                            // Handle other socket errors
                            _client.Disconnect();
                            return;
                        }
                    }

                    if (streamLength <= 0)
                    {
                        _client.Disconnect();
                        return;
                    }

                    _currentDataLength += streamLength;
                    while (true)
                    {
                        //if this is 0, then it means we have to read it from start of the buffer
                        if (_currentMessageLength == 0)
                        {
                            if (_currentDataLength >= 4)
                                _currentMessageLength = BitConverter.ToInt32(_receiveBuffer, 0);
                        }

                        if (_currentMessageLength > 0 && _currentDataLength >= _currentMessageLength)
                        {
                            byte[] packet = new byte[_currentMessageLength - 4];
                            Array.Copy(_receiveBuffer, 4, packet, 0, packet.Length);

                            if (_handler.ReadPacket(packet) == false)
                            {
                                _client.Disconnect();
                                return;
                            }

                            int reducedBufferedDataLength = _currentDataLength - _currentMessageLength;
                            Array.Copy(_receiveBuffer, _currentMessageLength, _receiveBuffer, 0, reducedBufferedDataLength);

                            _currentDataLength = reducedBufferedDataLength;
                            _currentMessageLength = 0;
                        }
                        else
                            break; //every message in stream read
                    }
                }
            }
        
            public void HandleConnectionWrite()
            {
                //after reading and making sure connection is alive, send what we stored to send
                while (_dataToSend.TryDequeue(out byte[] packet))
                {
                    if (_networkStream == null)
                    {
                        _client.EasyClientAPI.Disconnect();
                        return;
                    }

                    try
                    {
                        _networkStream.Write(packet, 0, packet.Length);
                    }
                    catch
                    {
                        _client.EasyClientAPI.Disconnect();
                        return;
                    }
                }
            }

            public void Disconnect(int code)
            {
                _isConnected = false;
                socket.Close();
                _networkStream.Close();
                _networkStream = null;
                _receiveBuffer = null;
                socket = null;

                _client.EasyClientAPI.Callback_OnDisconnected?.Invoke();
                _client.ClientStatus = ConnectionStatus.NotConnected;
            }

            public void SendData(byte[] data)
            {
                _dataToSend.Enqueue(data);
            }
        }
    }
}
