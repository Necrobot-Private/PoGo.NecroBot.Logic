﻿namespace PoGo.NecroBot.Logic.Service.WebSocketHandler
{
    internal interface IWebSocketResponce
    {
        string RequestID { get; }
        string Command { get; }
        dynamic Data { get; }
    }
}