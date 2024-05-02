using System;

namespace EasyComClient
{
    //id: 0
    [Serializable]
    public struct CommandMsg
    {
        public ushort ReqID;
        public int CmdHash;
        public string Payload;
    }

    public struct ResponseMsg
    {
        public ushort ReqID;
        public byte Code;
        public string Payload;
    }

}