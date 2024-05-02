using DNUploader.Extensions;
using System.Collections.Concurrent;
using System.Xml.Serialization;


namespace EasyComServer
{
    internal class MessageConverter : IMessageConverter
    {
        //cache serializers in order to not create them during runtime every time app needs to serialize message to xml
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
            if(!_serializersHashes.TryGetValue(typeof(T), out ushort messageID))
                messageID = RegisterSerializer<T>();

            byte[] structData = SerializeToByteArray(obj);
            byte[] packet = new byte[structData.Length + 6];

            Array.Copy(BitConverter.GetBytes(packet.Length), 0, packet, 0, 4); //prefix message length
            Array.Copy(BitConverter.GetBytes(messageID), 0, packet, 4, 2); //prefix messege id
            Array.Copy(structData, 0, packet, 6, structData.Length); //set payload after those two prefixes
            return packet;
        }

        public byte[] SerializeToByteArray<T>(T obj) where T : struct
        {
            using (var ms = new MemoryStream())
            {
                _serializers[typeof(T)].Serialize(ms, obj);
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