namespace EasyComClient
{
    internal interface IClientHandler
    {
        bool ReadPacket(byte[] packet);
        void RegisterMessageHandler<T>(EasyClientAPI.MessageHandler<T> handler);
    }
}