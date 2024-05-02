namespace EasyComServer
{
    internal interface IServerHandler
    {
        bool ReadPacket(short clientID, byte[] packet);
        void RegisterMessageHandler<T>(MessageHandler<T> handler);
    }
}