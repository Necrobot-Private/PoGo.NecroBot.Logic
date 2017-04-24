﻿namespace PoGo.NecroBot.Logic.Service.WebSocketHandler.GetCommands.Events
{
    public class ConfigResponce : IWebSocketResponce
    {
        public ConfigResponce(dynamic data, string requestID)
        {
            Command = "ConfigWeb";
            Data = data;
            RequestID = requestID;
        }

        public string RequestID { get; }
        public string Command { get; }
        public dynamic Data { get; }
    }
}