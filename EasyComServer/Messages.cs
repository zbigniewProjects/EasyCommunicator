namespace EasyComServer
{
    #region universal messages
    [Serializable]
    public struct CommandMsg
    {
        public ushort ReqID;
        public int CmdHash; //equivalent of endpoint in rest app
        public string Payload; //equivalent of body in rest app
    }

    public struct ResponseMsg
    {
        public ushort ReqID;
        public byte Code;
        public string Payload;

        public ResponseMsg()
        {
            Code = 255;
            ReqID = ushort.MaxValue;
            Payload = string.Empty;
        }
    }
    #endregion
}