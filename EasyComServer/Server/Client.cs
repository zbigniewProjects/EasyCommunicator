using System.Collections.Concurrent;
using System.Net.Sockets;
using LogSystem;
using Newtonsoft.Json;

namespace EasyComServer
{
    /// <summary>
    /// Instance of client that is connected to us
    /// </summary>
    internal class Client : IClient
    {
        public ConnectionStatus ClientStatus { get; set; } = ConnectionStatus.NotConnected;

        Server _server;

        public short ID() => _id;

        public static int DataBufferSize = 1024 * 1024;

        short _id = -1; //will be set after reading connect hash from client

        TCP _tcp;

        internal delegate void ReceivedResponseCallback(Request res);
        internal ConcurrentDictionary<ushort, ReceivedResponseCallback> _pendingRequests { private set; get; } = new ConcurrentDictionary<ushort, ReceivedResponseCallback>();
        private ushort _requestCounter;

        EasyServerAPI _easyServerApi;

        IMessageConverter _messageConverter;

        private object _reqCounterLock = new object();

        public Client(EasyServerAPI dNCommunicatorAPI, IServerHandler handler, short id, IMessageConverter messageConverter)
        {
            _tcp = new TCP(dNCommunicatorAPI.Logger, dNCommunicatorAPI, this, handler);
            _easyServerApi = dNCommunicatorAPI;
            _id = id;
            _messageConverter = messageConverter;
        }

        public void SetID(short id) => _id = id; 

        public void Connect(TcpClient _socket, Server server)
        {
            if (ClientStatus == ConnectionStatus.NotConnected)
            {
                ClientStatus = ConnectionStatus.Connecting;
                _server = server;
                _tcp.Connect(_socket, _server);
            }
        }

        public void Disconnect()
        {
            if (ClientStatus == ConnectionStatus.Connected)
            {
                ClientStatus = ConnectionStatus.Disconnecting;
            }
            else
                _easyServerApi.Logger.LogWarning("Already disconnected");
        }

        public void HandleStream()
        {
            if (ClientStatus == ConnectionStatus.Disconnecting) 
            {
                //timeout all pending request before disconnecting
                foreach (ReceivedResponseCallback item in _pendingRequests.Values)
                {
                    item.Invoke(new Request { Code = 254 });
                }

                _easyServerApi.Logger.LogInfo($"Disconnecting client {ID()} due to server closure.");

                _tcp.Disconnect();
                _server.OnDisconnectClient(_id);
                ClientStatus = ConnectionStatus.NotConnected;
                return;
            }


            if (ClientStatus == ConnectionStatus.Connected)
            {
                _tcp.HandleStreamRead();
                _tcp.HandleStreamWrite();
            }
        }

        #region accessors
        public void SendMessage<T>(T msg) where T : struct
        {
            if (ClientStatus != ConnectionStatus.Connected && _easyServerApi.Configuration.ThrowException_WhenSendingDataWhileNotConnected)
                throw new Exception("Can not send data while client is not connected");

            /*There is always a possibility that unity reload our client while we communicating with it, so before
            sending any data always check if connection exists*/
            if (ClientStatus != ConnectionStatus.Connected) return;

            _tcp.SendData(_messageConverter.SerializeWithMsgID(msg));
        }
        #region request system
        public async Task<Request> SendRequest(string name, string data)
        {
            if (_easyServerApi.Configuration.ThrowException_WhenSendingRequestWhileNotConnected &&
                ClientStatus != ConnectionStatus.Connected)
                throw new Exception("Cannot send command when client is not connected to the server");

            Request res = new Request();
            res.Code = byte.MaxValue; //code 255 means request timeout. This wont be overriden if we don't receive response, so timeout
            
            //if request was made when client is not connected return timeout response
            if (ClientStatus != ConnectionStatus.Connected) return res;

            object _requestlock = new object();

            ushort reqID;
            lock (_reqCounterLock)
            {
                reqID = _requestCounter;

                _pendingRequests.TryAdd(reqID, ReceivedResponseCallback);

                _requestCounter++;
                if (_requestCounter == ushort.MaxValue)
                    _requestCounter = 0;
            }
            //send message to client
            SendMessage(new CommandMsg
            {
                CmdHash = _server.CommandManager.GetExistingPostCmdHash(name),
                Payload = data,
                ReqID = reqID
            });

            //wait for respose from client or timeout request if no response will be received
            return await Task.Run(() =>
            {
                lock (_requestlock)
                {
                    Monitor.Wait(_requestlock, _easyServerApi.Configuration.RequestTimeout);
                }

                _pendingRequests.TryRemove(reqID, out ReceivedResponseCallback value);

                return res;
            });

            void ReceivedResponseCallback(Request _res)
            {
                res = _res;

                lock (_requestlock)
                {
                    Monitor.Pulse(_requestlock);
                }
            }
        }
        public async Task<Request> SendStructuredRequest<T>(string name, T msg) where T : struct
        {
            string serializedMsg = JsonConvert.SerializeObject(msg);
            return await SendRequest(name, serializedMsg);
        }

        public void EvaluateResponse(ResponseMsg res)
        {
            if (_pendingRequests.TryRemove(res.ReqID, out Client.ReceivedResponseCallback value))
                value.Invoke(new Request { Code = res.Code, Payload =  res.Payload});
            else
                _easyServerApi.Callback_OnReceivedOutdatedResponse?.Invoke(new Request { Code = res.Code, Payload =  res.Payload});
        }
        #endregion

        #endregion

        /// <summary>
        /// Class for managing tcp connection. 
        /// </summary>
        class TCP
        {
            IClient _client;
            TcpClient _socket;
            NetworkStream _stream;
            IServerHandler _handler;

            byte[] _receiveBuffer;

            ILogger _logger;

            EasyServerAPI _dnCommunicatorAPI;

            ConcurrentQueue<byte[]> _dataToSend = new ConcurrentQueue<byte[]>();

            int _currentMessageLength = 0;
            int _bufferedDataLength = 0;

            public TCP(ILogger logger, EasyServerAPI dNCommunicatorAPI, IClient client, IServerHandler handler)
            {
                _client = client;
                _handler = handler;
                _logger = logger;
                _dnCommunicatorAPI = dNCommunicatorAPI;

                _receiveBuffer = new byte[DataBufferSize * 2];
            }

            public async void Connect(TcpClient socket, Server server)
            {
                _socket = socket;
                _socket.ReceiveBufferSize = DataBufferSize;
                _socket.SendBufferSize = DataBufferSize;

                _stream = _socket.GetStream();
                _stream.ReadTimeout = 50;
                _stream.WriteTimeout = 50;

                _client.ClientStatus = ConnectionStatus.Connected;

                byte[] data = new byte[2];
                int byteLength = 0;

                while (byteLength < 2)
                {
                    try
                    {
                        byteLength = _stream.Read(_receiveBuffer, 0, 2);
                    }
                    catch
                    {
                        //client disconnected during establishing a connection;
                        continue;
                    }
                    await Task.Delay(20);
                }

                if (byteLength < 2) 
                {
                    _client.Disconnect();
                    return;
                }

                //read hash, if exist in queue connect and assign id
                short appID = BitConverter.ToInt16(data);

                if (server.UseSeatSystem && server.Clients.TryGetValue(appID, out IClient c))
                {
                    _logger.LogWarning($"Client with seat id {appID} is already connected, disconnecting redundant");

                    _dnCommunicatorAPI.Callback_OnDoubleConnectionDetected?.Invoke(appID);
                    _client.Disconnect();
                    return;
                }

                if (server.UseSeatSystem && !server.WaitingSeats.Contains(appID))
                {
                    _logger.LogWarning($"Client with seat id {appID} is not expected to connect, disconnecting");

                    _dnCommunicatorAPI.Callback_OnUnexpectedClientTriedToConnect?.Invoke(appID);
                    _client.Disconnect();
                    return;
                }

                if (server.UseSeatSystem)
                    server.RemoveClientSeat(appID);

                _dnCommunicatorAPI.Callback_OnClientConnected?.Invoke(_client);

                server.Clients.TryAdd(_client.ID(), _client);
            }

            public void HandleStreamRead()
            {
                int streamLength = DataBufferSize;

                while (streamLength == DataBufferSize)
                {
                    if (_client.ClientStatus != ConnectionStatus.Connected) return;

                    try
                    {
                        streamLength = _stream.Read(_receiveBuffer, _bufferedDataLength, DataBufferSize);
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

                    _bufferedDataLength += streamLength;
                    while (true)
                    {
                        //if this is 0, then it means we have to read it from start of the buffer
                        if (_currentMessageLength == 0)
                        {
                            if (_bufferedDataLength >= 4)
                                _currentMessageLength = BitConverter.ToInt32(_receiveBuffer, 0);
                        }

                        if (_currentMessageLength > 0 && _bufferedDataLength >= _currentMessageLength)
                        {
                            byte[] packet = new byte[_currentMessageLength - 4];
                            Array.Copy(_receiveBuffer, 4, packet, 0, packet.Length);

                            if (_handler.ReadPacket(_client.ID(), packet) == false)
                            {
                                _client.Disconnect();
                                return;
                            }

                            int reducedBufferedDataLength = _bufferedDataLength - _currentMessageLength;
                            Array.Copy(_receiveBuffer, _currentMessageLength, _receiveBuffer, 0, reducedBufferedDataLength);

                            _bufferedDataLength = reducedBufferedDataLength;
                            _currentMessageLength = 0;
                        }
                        else
                            break; //every message in stream read
                    }
                }
            }

            public void HandleStreamWrite()
            {
                //after reading and making sure connection is alive, send what we stored to send
                while (_dataToSend.TryDequeue(out byte[] packet))
                {
                    if (_client.ClientStatus != ConnectionStatus.Connected) return;

                    try
                    {
                        int msgLength = BitConverter.ToInt32(packet, 0);
                        _stream.Write(packet, 0, packet.Length);
                    }
                    catch
                    {
                        _client.Disconnect();
                        return;
                    }
                }
            }

            internal void SendData(byte[] data)
            {
                _dataToSend.Enqueue(data);
            }

            public void Disconnect()
            {
                _socket.Close();
                _socket = null;
                _stream.Close();
                _stream = null;
            }
        }
    }
}
