using DNUploader.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;


namespace EasyComClient
{
    internal class MessageConverter : IMessageConverter
    {
        //serializers may be registered during application runtime, by sending data that is not registered app will register those automatically, but register
        //function may be called from different threads and concurrencies
        ConcurrentDictionary<Type, XmlSerializer> _serializers = new ConcurrentDictionary<Type, XmlSerializer>();
        ConcurrentDictionary<Type, ushort> _serializersHashes = new ConcurrentDictionary<Type, ushort>();

        Dictionary<ushort, XmlSerializer> _deSerializers = new Dictionary<ushort, XmlSerializer>();

        public object DeserializeAsObj(byte[] byteArray, ushort messageID)
        {
            using (var memStream = new MemoryStream(byteArray))
            {
                var serializer = _deSerializers[messageID];
                var obj = serializer.Deserialize(memStream);
                return obj;
            }
        }

        public byte[] SerializeWithMsgID<T>(T obj) where T : struct
        {
            if (!_serializersHashes.TryGetValue(typeof(T), out ushort messageID))
                messageID = RegisterSerializer<T>();

            byte[] structData = SerializeToByteArray(obj);
            byte[] packet = new byte[structData.Length + 6];

            Array.Copy(BitConverter.GetBytes(packet.Length), 0, packet, 0, 4); //prefix message length
            Array.Copy(BitConverter.GetBytes(messageID), 0, packet, 4, 2); //prefix messege id
            Array.Copy(structData, 0, packet, 6, structData.Length); //set payload after those two prefixes

            return packet;
        }

        byte[] SerializeToByteArray<T>(T obj) where T : struct
        {
            using (var ms = new MemoryStream())
            {
                var serializer = _serializers[typeof(T)];
                serializer.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        ushort RegisterSerializer<T>()
        {
            ushort id = typeof(T).Name.GetStableHashCode16();
            _serializersHashes.TryAdd(typeof(T), id);
            _serializers.TryAdd(typeof(T), new XmlSerializer(typeof(T)));
            return id;
        }
        public ushort RegisterDeserializer<T>()
        {
            ushort deserializerID = typeof(T).Name.GetStableHashCode16();

            _deSerializers.Add(deserializerID, new XmlSerializer(typeof(T)));
            return deserializerID;
        }
    }
}