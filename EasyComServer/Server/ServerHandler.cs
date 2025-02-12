namespace EasyComServer
{
    /// <summary>
    /// class that distributes incoming data from client
    /// </summary>
    internal class ServerHandler : IServerHandler
    {
        Dictionary<ushort, Delegate> _packetHandlers = new Dictionary<ushort, Delegate>();
        IMessageConverter _messageConverter;
        public ServerHandler(IMessageConverter structMessageManager)
        {
            _messageConverter = structMessageManager;
        }

        public void RegisterMessageHandler<T>(MessageHandler<T> handler)
        {
            ushort msgID = _messageConverter.RegisterDeserializer<T>();
            _packetHandlers.Add(msgID, handler);
        }

        public bool ReadPacket(short clientID, byte[] packet)
        {
            ushort msgID = BitConverter.ToUInt16(packet, 0);
            byte[] data = new byte[packet.Length - 2];
            Array.Copy(packet, 2, data, 0, data.Length);

            if (_packetHandlers.TryGetValue(msgID, out Delegate handler))
            {
                handler.DynamicInvoke(clientID, _messageConverter.DeserializeAsObj(data, msgID));
                return true;
            }
            else
            {
                //_logger.LogWarning("Client sent unrecognized packet, disconnecting...");
                return false;
            }
        }
    }
}
