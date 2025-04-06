using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace EasyComClient
{
    public enum DisconnectCause
    {
        Disconnected,
        ServerDisconnected,
        ServerSentInvalidMessage,
        ServerSentInvalidHandshake,
    }
    internal class Client
    {
        enum ConnectionStatus
        {
            NotConnected,
            Connecting,
            Connected,
            Disconnecting,
        }

        ConnectionStatus ClientStatus { get; set; } = ConnectionStatus.NotConnected;

        public TCP Tcp;
        public short SeatHash;
        
        IMessageConverter _messageConverter;

        public EasyClientAPI EasyClientAPI;
        CommandSystem _commandSystem;

        internal delegate void ReceivedResponseCallback(Request res);
        internal ConcurrentDictionary<ushort, ReceivedResponseCallback> _pendingRequests { get; set; } = new ConcurrentDictionary<ushort, ReceivedResponseCallback>();
        ushort _requestCounter;

        DisconnectCause _disconnectCause;

        public Client(EasyClientAPI api, CommandSystem commandSystem, IMessageConverter messageConverter, IClientHandler handler)
        {
            EasyClientAPI = api;
            _messageConverter = messageConverter;
            _commandSystem = commandSystem;

            Tcp = new TCP(api, this, handler);
        }

        public async Task<bool> ConnectToServer(short seatHash, string serverAddress, ushort port)
        {
            SeatHash = seatHash;
            return await Tcp.Connect(seatHash, serverAddress, port);
        }

        public void Disconnect(DisconnectCause cause) 
        {
            _disconnectCause = cause;
            //set connection status to disconnecting so thread that processes network stream can detect it and safely handle disconnection
            ClientStatus = ConnectionStatus.Disconnecting;
        }

        public void HandleConnection()
        {
            if (ClientStatus == ConnectionStatus.NotConnected) 
                return;

            if (ClientStatus == ConnectionStatus.Disconnecting)
            {
                //timeout all pending request before disconnecting
                foreach (ReceivedResponseCallback item in _pendingRequests.Values)
                {
                    item.Invoke(new Request { Code = 254 });
                }

                Tcp.Disconnect(_disconnectCause);
                EasyClientAPI.Status = EasyComClient.ClientStatus.NotConnected;
                return;
            }

            Tcp.HandleConnectionRead();
            Tcp.HandleConnectionWrite();
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
                //is user somehow accumulate over 65k requests, just make later requests timeout
                return res;
            }

            ushort reqID = _requestCounter;
            SemaphoreSlim semaphoreSlim = new SemaphoreSlim(0);

            _pendingRequests.TryAdd(reqID, ReceivedResponseCallback);

            _requestCounter++;
            if (_requestCounter == ushort.MaxValue)
                _requestCounter = 0;

            //send message to client
            SendMessage(new CommandMsg
            {
                CmdHash = _commandSystem.GetExistingPostCmdHash(name),
                Payload = data,
                ReqID = reqID
            });

            await semaphoreSlim.WaitAsync(TimeSpan.FromMilliseconds(EasyClientAPI.Configuration.RequestTimeout));
            _pendingRequests.TryRemove(reqID, out ReceivedResponseCallback value);

            return res;

            void ReceivedResponseCallback(Request _res)
            {
                res = _res;
                semaphoreSlim.Release();
                semaphoreSlim.Dispose();
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
            public TcpClient socket;
            NetworkStream _networkStream;
            private byte[] _receiveBuffer;

            bool _isConnected;

            short AppID;
            IClientHandler _handler;

            public static int DataBufferSize = 1024 * 1024;

            ConcurrentQueue<byte[]> _dataToSend = new ConcurrentQueue<byte[]>();

            int _currentMessageLength = 0;
            int _currentDataLength = 0;

            Client _client;
            EasyClientAPI _easyClientAPI;

            public TCP(EasyClientAPI easyClientAPI, Client client, IClientHandler handler)
            {
                _handler = handler;
                _client = client;
                _easyClientAPI = easyClientAPI;
            }

            public async Task<bool> Connect(short appID, string serverAddress, ushort port)
            {
                _easyClientAPI.Status = EasyComClient.ClientStatus.EstablishingConnection;

                if (_client.ClientStatus == ConnectionStatus.Connecting)
                    return false;

                if (_client.ClientStatus == ConnectionStatus.Disconnecting)
                    return false;

                if (_client.ClientStatus == ConnectionStatus.Connected)
                    return false;

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
                    //either wait for establishing connection to complete, or wait timeout time set in config
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

                        //this will start loop that will read and write data from/to network stream
                        _easyClientAPI.StartConnection(); 
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
                        if (socketEx.SocketErrorCode == SocketError.WouldBlock)
                        {
                            return;
                        }
                        if (socketEx.SocketErrorCode == SocketError.TimedOut)
                        {
                            // Handle write timeout exception
                            return;
                        }
                        else if (socketEx.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            _easyClientAPI.Log($"SOCKET ERROR:{socketEx.SocketErrorCode}:{socketEx.Message}");
                            // Handle remote host closure
                            _client.Disconnect(DisconnectCause.ServerDisconnected);
                            return;
                        }
                        else
                        {
                            _easyClientAPI.Log($"SOCKET ERROR:{socketEx.SocketErrorCode}:{socketEx.Message}");
                            // Handle other socket errors
                            _client.Disconnect(DisconnectCause.ServerDisconnected);
                            return;
                        }
                    }

                    if (streamLength <= 0)
                    {
                        _easyClientAPI.Log($"Stream length {streamLength}");
                        _client.Disconnect(DisconnectCause.ServerDisconnected);
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
                                _client.Disconnect(DisconnectCause.ServerSentInvalidMessage);
                                return;
                            }

                            int reducedBufferedDataLength = _currentDataLength - _currentMessageLength;
                            Array.Copy(_receiveBuffer, _currentMessageLength, _receiveBuffer, 0, reducedBufferedDataLength);

                            _currentDataLength = reducedBufferedDataLength;
                            _currentMessageLength = 0;
                        }
                        else
                            break; //every message in stream read, so break loop
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

            public void Disconnect(DisconnectCause cause)
            {
                _isConnected = false;
                socket.Close();
                _networkStream.Close();
                _networkStream = null;
                _receiveBuffer = null;
                socket = null;

                _client.EasyClientAPI.Callback_OnDisconnected?.Invoke(cause);
                _client.ClientStatus = ConnectionStatus.NotConnected;
            }

            public void SendData(byte[] data)
            {
                _dataToSend.Enqueue(data);
            }
        }
    }
}
