using DNUploader.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace EasyComClient
{
    internal class CommandSystem
    {
        //commands that we are asked to be executed by client
        Dictionary<int, CommandBaseClient> _commands = new Dictionary<int, CommandBaseClient>();
        Dictionary<string, int> _cmdHashes = new Dictionary<string, int>();

        //commands that we will ask client to run (we only need their hashes)
        ConcurrentDictionary<string, int> _postCmdHashes = new ConcurrentDictionary<string, int>();

        
        IClientHandler _clientHandler;

        EasyClientAPI _clientApi;
        public CommandSystem (EasyClientAPI client, IClientHandler clientHandler)
        {            
            _clientHandler = clientHandler;
            _clientApi = client;

            _clientHandler.RegisterMessageHandler<CommandMsg>(ReadCommand);
            _clientHandler.RegisterMessageHandler<ResponseMsg>(Read_ResponseFromServer);
        }

        public int GetExistingPostCmdHash(string cmdName)
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

        public void RegisterCommand(string name, CommandBaseClient method)
        {
            int hash = name.CustomStringHash();

            if (_commands.TryGetValue(hash, out CommandBaseClient cmd))
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

                //_logger.LogWarning($"Command \"{name}\" produces the same hash as \"{previousCmdName}\", please rename one of them");
                return;
            }

            _commands.Add(hash, method);
            _cmdHashes.Add(name, hash);
        }


        void Read_ResponseFromServer(ResponseMsg data)
        {
            //check is response is still current, since it can always be received after passing of timeout
            if (_clientApi.Client._pendingRequests.TryRemove(data.ReqID, out Client.ReceivedResponseCallback value))
                value.Invoke(new Request { Code = data.Code, ResponseBody = data.Payload });
        }

        public void ReadCommand(CommandMsg cmd)
        {
            CommandBaseClient method;
            if (_commands.TryGetValue(cmd.CmdHash, out method))
            {
                method.Invoke(cmd.Payload, new Response(Respond));
                _clientApi.Log($"Received command: {cmd.CmdHash}:{cmd.Payload}");
            }
            else
                _clientApi.Log($"Received request from server that is not recognized {cmd.CmdHash} wit payload {cmd.Payload}");

            void Respond(byte code, string body)
            {
                _clientApi.Client.SendMessage(new ResponseMsg { ReqID = cmd.ReqID, Code = code, Payload = body });
            }
        }
    }
}
