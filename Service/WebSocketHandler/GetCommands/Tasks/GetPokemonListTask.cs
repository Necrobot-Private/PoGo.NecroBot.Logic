﻿#region using directives

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Service.WebSocketHandler.GetCommands.Events;
using PoGo.NecroBot.Logic.Service.WebSocketHandler.GetCommands.Helpers;
using PoGo.NecroBot.Logic.State;
using SuperSocket.WebSocket;

#endregion

namespace PoGo.NecroBot.Logic.Service.WebSocketHandler.GetCommands.Tasks
{
    internal class GetPokemonListTask
    {
        public static async Task Execute(ISession session, WebSocketSession webSocketSession, string requestID)
        {
            //using (var blocker = new BlockableScope(session, BotActions.ListItems))
            {
                //if (!await blocker.WaitToRun()) return;

                var allPokemonInBag = await session.Inventory.GetHighestsCp(1000);
                var list = new List<PokemonListWeb>();
                allPokemonInBag.ToList().ForEach(o => list.Add(new PokemonListWeb(session, o)));
                webSocketSession.Send(EncodingHelper.Serialize(new PokemonListResponce(list, requestID)));
            }
        }
    }
}