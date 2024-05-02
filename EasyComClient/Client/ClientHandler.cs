using DNUploader.Extensions;
using System;
using System.Collections.Generic;
using static EasyComClient.EasyClientAPI;

namespace EasyComClient
{
    internal class ClientHandler : IClientHandler
    {
        Dictionary<ushort, Delegate> _packetHandlers = new Dictionary<ushort, Delegate>();

        IMessageConverter _converter;

        public void Init(IMessageConverter messageConverter) => _converter = messageConverter;

        public void RegisterMessageHandler<T>(MessageHandler<T> handler)
        {
            ushort msgID = _converter.RegisterDeserializer<T>();
            _packetHandlers.Add(msgID, handler);
        }

        public bool ReadPacket(byte[] packet)
        {
            ushort msgID = BitConverter.ToUInt16(packet, 0);
            byte[] structData = new byte[packet.Length - 2];
            Array.Copy(packet, 2, structData, 0, structData.Length);

            if (_packetHandlers.TryGetValue(msgID, out var handler))
            {
                handler.DynamicInvoke(_converter.DeserializeAsObj(structData, msgID));
                return true;
            }
            else return false;
        }
    }
}
