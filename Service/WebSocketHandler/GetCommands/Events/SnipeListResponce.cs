﻿namespace PoGo.NecroBot.Logic.Service.WebSocketHandler.GetCommands.Events
{
    internal class SnipeListResponce : IWebSocketResponce
    {
        public SnipeListResponce(dynamic data, string requestID)
        {
            Command = "HumanWalkSnipEvent";
            Data = data;
            RequestID = requestID;
        }

        public string RequestID { get; }
        public string Command { get; }
        public dynamic Data { get; }
    }
}