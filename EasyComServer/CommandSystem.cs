using DNUploader.Extensions;
using System.Collections.Concurrent;

namespace EasyComServer
{
    public delegate void ResponseBase(byte code, string body = "");

    internal class CommandSystem
    {
        //commands that we are asked to execute by client
        Dictionary<int, CommandBase> _commands = new Dictionary<int, CommandBase>();

        //commands are identified based on integer hashes of their names, instead of names themselves in order to safe bandwidth
        Dictionary<string, int> _cmdHashes = new Dictionary<string, int>();

        //commands that we will ask client to run (we only need their hashes)
        ConcurrentDictionary<string, int> _postCmdHashes = new ConcurrentDictionary<string, int>();

        EasyServerAPI _serverInterface;
        IServerHandler _serverHandler;


        public CommandSystem(EasyServerAPI dnComInterface, IServerHandler serverHandler)
        {
            _serverInterface = dnComInterface;
            _serverHandler = serverHandler;

            //what we expect to receive
            _serverHandler.RegisterMessageHandler<CommandMsg>(Read_ReadCommand);

            //register serializer for response message that we will be using to respond to client command requests
            _serverHandler.RegisterMessageHandler<ResponseMsg>(Read_Response);
        }

        public void Read_ReadCommand(short clientID, CommandMsg packet)
        {
            CommandBase method;
            if (_commands.TryGetValue(packet.CmdHash, out method))
            {
                method.Invoke(clientID, packet.Payload, new Response(Respond));
            }
            else
                _serverInterface.Logger.LogError($"Request received from {clientID} is not recognized, req hash {packet.CmdHash}");

            void Respond(byte code, string body)
            {
                //its possible that client will be long gone before we send him a response, so check if he is still around before responding
                if (_serverInterface.Clients.TryGetValue(clientID, out IClient c))
                    c.SendMessage(new ResponseMsg { ReqID = packet.ReqID, Code = code, Payload = body });
            }
        }
        public void Read_Response(short clientID, ResponseMsg res)
        {
            _serverInterface.Clients[clientID].EvaluateResponse(res);
        }


        internal int GetExistingPostCmdHash(string cmdName)
        {
            if (_postCmdHashes.TryGetValue(cmdName, out int hash))
                return hash;
            else
            {
                int newHash = cmdName.CustomStringHash();
                _postCmdHashes.TryAdd(cmdName, newHash);
                return newHash;
            }
        }

        public void RegisterCommand(string name, CommandBase method)
        {
            int hash = name.CustomStringHash();

            if (_commands.TryGetValue(hash, out CommandBase cmd)) 
            {
                //registered command with same hash found, now find its name so we can point it out to the user

                string previousCmdName = string.Empty;
                foreach (var pair in _cmdHashes)
                {
                    if (pair.Value == hash)
                    {
                        previousCmdName = pair.Key;
                        break;
                    }
                }

                _serverInterface.Logger.LogError($"Command \"{name}\" produces the same hash as \"{previousCmdName}\", please rename one of them");
                return;
            }

            //register command and accessor hash for it
            _commands.Add(hash, method);
            _cmdHashes.Add(name, hash);
        }
    }
}
