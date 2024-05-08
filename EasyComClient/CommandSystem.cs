using DNUploader.Extensions;
using LogSystem;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace EasyComClient
{
    internal class CommandSystem
    {
        //commands that we are asked to be executed by client
        Dictionary<int, CommandBaseClient> _commands = new Dictionary<int, CommandBaseClient>();
        Dictionary<string, int> _cmdHashes = new Dictionary<string, int>();

        //commands that we will ask client to run (we only need their hashes)
        ConcurrentDictionary<string, int> _postCmdHashes = new ConcurrentDictionary<string, int>();

        ILogger _logger;
        IClientHandler _clientHandler;

        EasyClientAPI _clientApi;
        public CommandSystem (ILogger logger, EasyClientAPI client, IClientHandler clientHandler)
        {
            _logger = logger;
            _clientHandler = clientHandler;
            _clientApi = client;

            _clientHandler.RegisterMessageHandler<CommandMsg>(ReadCommand);
            _clientHandler.RegisterMessageHandler<ResponseMsg>(Read_ResponseFromServer);
        }

        void Read_ResponseFromServer(ResponseMsg data)
        {
            //check is response is still current, since it can always be received after passing of timeout
            if (_clientApi.Client._pendingRequests.TryRemove(data.ReqID, out Client.ReceivedResponseCallback value))
                value.Invoke(new Request { Code = data.Code, ResponseBody = data.Payload});
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

                _logger.LogWarning($"Command \"{name}\" produces the same hash as \"{previousCmdName}\", please rename one of them");
                return;
            }

            _commands.Add(hash, method);
            _cmdHashes.Add(name, hash);
        }

        public void ReadCommand(CommandMsg cmd)
        {
            CommandBaseClient method;
            if (_commands.TryGetValue(cmd.CmdHash, out method))
            {
                method.Invoke(cmd.Payload, new Response(Respond));
            }
            else
                _logger.LogWarning($"Received request from server that is not recognized {cmd.CmdHash}");

            void Respond(byte code, string body)
            {
                _clientApi.Client.SendMessage(new ResponseMsg { ReqID = cmd.ReqID, Code = code, Payload = body });
            }
        }
    }
}
