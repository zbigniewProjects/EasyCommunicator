using System.Net.Sockets;

namespace EasyComServer
{
    public interface IClient
    {
        ConnectionStatus ClientStatus { get; set; }
        public short ID (); //will be set after reading connect hash from client
        internal void Connect(TcpClient _socket, Server server);

        void SetID(short ID);

        void SendMessage<T>(T msg) where T : struct;

        void Disconnect(DisconnectCause cause);
        Task<Request> SendRequest(string name, string data);
        Task<Request> SendStructuredRequest<T>(string name, T msg) where T : struct;

        void EvaluateResponse(ResponseMsg res);

        public void HandleStream();


    }

    public enum DisconnectCause 
    {
        Disconnected, //server ordered disconnection
        ClientDisconnected, //client disconnected by itself
        ClientSentInvalidHandshake,
        ClientSentInvalidMessage,
        ClientWasNotExpected,
        ClientWithSameTickedIsAlreadyConnected,
    }

    public enum ConnectionStatus
    {
        NotConnected,
        Connecting,
        Connected,
        Disconnecting,
    }
}