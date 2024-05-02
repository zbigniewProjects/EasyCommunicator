namespace EasyComClient
{
    internal interface IMessageConverter
    {
        object DeserializeAsObj(byte[] byteArray, ushort messageID);
        ushort RegisterDeserializer<T>();
        byte[] SerializeWithMsgID<T>(T obj) where T : struct;
    }
}