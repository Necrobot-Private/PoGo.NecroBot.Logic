﻿using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using POGOProtos.Data;
using POGOProtos.Networking.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PoGo.NecroBot.Logic.Tasks
{
    public class UseRareCandyTask
    {
        public static async Task Execute(ISession session, PokemonData pokemondata)
        {
            var candy = await session.Inventory.GetCandyCount(pokemondata.PokemonId).ConfigureAwait(false);
            var response = await session.Client.Inventory.UseItemMoveReroll(pokemondata.Id).ConfigureAwait(false);
            switch (response.Result)
            {
                case ReleasePokemonResponse.Types.Result.Success:
                    Logger.Write($"Success {candy} ===> {candy + 1}");
                    break;
                case ReleasePokemonResponse.Types.Result.Failed:
                    Logger.Write($"Failed to use RareCandy!", LogLevel.Error);
                    break;
                case ReleasePokemonResponse.Types.Result.ErrorPokemonIsBuddy:
                    break;
                case ReleasePokemonResponse.Types.Result.ErrorPokemonIsEgg:
                    break;
                case ReleasePokemonResponse.Types.Result.PokemonDeployed:
                    break;
                case ReleasePokemonResponse.Types.Result.Unset:
                    break;
                default:
                    Logger.Write($"Failed to use MoveReroll!", LogLevel.Warning);
                    break;
            }
        }
    }
}