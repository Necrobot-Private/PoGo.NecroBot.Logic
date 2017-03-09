﻿#region using directives

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Exceptions;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using POGOProtos.Enums;
using POGOProtos.Inventory.Item;
using POGOProtos.Map.Pokemon;
using POGOProtos.Networking.Responses;
using TinyIoC;
using System.Collections.Generic;
using System.Device.Location;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public static class CatchNearbyPokemonsTask
    {
        //add delegate
        public delegate void PokemonsEncounterDelegate(List<MapPokemon> pokemons);

        public static async Task Execute(ISession session, CancellationToken cancellationToken,
            PokemonId priority = PokemonId.Missingno, bool sessionAllowTransfer = true)
        {
            TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
            cancellationToken.ThrowIfCancellationRequested();

            if (!session.LogicSettings.CatchPokemon) return;

            var totalBalls = session.Inventory.GetItems().Where(x => x.ItemId == ItemId.ItemPokeBall || x.ItemId == ItemId.ItemGreatBall || x.ItemId == ItemId.ItemUltraBall).Sum(x => x.Count);

            if (session.SaveBallForByPassCatchFlee && totalBalls < 130)
            {
                return ;
            }

            if (session.Stats.CatchThresholdExceeds(session))
            {
                if (session.LogicSettings.AllowMultipleBot &&
                    session.LogicSettings.MultipleBotConfig.SwitchOnCatchLimit &&
                    TinyIoCContainer.Current.Resolve<MultiAccountManager>().AllowSwitch()
                    )
                {
                    throw new ActiveSwitchByRuleException()
                    {
                        MatchedRule = SwitchRules.CatchLimitReached,
                        ReachedValue = session.LogicSettings.CatchPokemonLimit
                    };
                }

                return;
            }

            Logger.Write(session.Translation.GetTranslation(TranslationString.LookingForPokemon), LogLevel.Debug);

            var nearbyPokemons = await GetNearbyPokemons(session);
            if (nearbyPokemons == null) return;
            var priorityPokemon = nearbyPokemons.Where(p => p.PokemonId == priority).FirstOrDefault();
            var pokemons = nearbyPokemons.Where(p => p.PokemonId != priority).ToList();

            //add pokemons to map
            OnPokemonEncounterEvent(pokemons.ToList());

            EncounterResponse encounter = null;
            //if that is snipe pokemon and inventories if full, execute transfer to get more room for pokemon
            if (priorityPokemon != null)
            {
                pokemons.Insert(0, priorityPokemon);
                LocationUtils.UpdatePlayerLocationWithAltitude(session,
                        new GeoCoordinate(priorityPokemon.Latitude, priorityPokemon.Longitude, session.Client.CurrentAltitude), 0); // Set speed to 0 for random speed.
                encounter = await session.Client.Encounter
                    .EncounterPokemon(priorityPokemon.EncounterId, priorityPokemon.SpawnPointId);

                if (encounter.Status == EncounterResponse.Types.Status.PokemonInventoryFull)
                {
                    if (session.LogicSettings.TransferDuplicatePokemon)
                        await TransferDuplicatePokemonTask.Execute(session, cancellationToken);

                    if (session.LogicSettings.TransferWeakPokemon)
                        await TransferWeakPokemonTask.Execute(session, cancellationToken);

                    if (session.LogicSettings.EvolveAllPokemonAboveIv ||
                        session.LogicSettings.EvolveAllPokemonWithEnoughCandy ||
                        session.LogicSettings.UseLuckyEggsWhileEvolving ||
                        session.LogicSettings.KeepPokemonsThatCanEvolve)
                    {
                        await EvolvePokemonTask.Execute(session, cancellationToken);
                    }
                }
            }

            foreach (var pokemon in pokemons)
            {
                await MSniperServiceTask.Execute(session, cancellationToken);
                
                if (LocationUtils.CalculateDistanceInMeters(pokemon.Latitude, pokemon.Longitude, session.Client.CurrentLatitude, session.Client.CurrentLongitude) > session.Client.GlobalSettings.MapSettings.EncounterRangeMeters)
                {
                    Logger.Debug($"THIS POKEMON IS TOO FAR, {pokemon.Latitude}, {pokemon.Longitude}");
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
                string pokemonUniqueKey = $"{pokemon.EncounterId}";

                if (session.Cache.GetCacheItem(pokemonUniqueKey) != null)
                {
                    continue; //this pokemon has been skipped because not meet with catch criteria before.
                }

                var allitems = session.Inventory.GetItems();
                var pokeBallsCount = allitems.FirstOrDefault(i => i.ItemId == ItemId.ItemPokeBall)?.Count;
                var greatBallsCount = allitems.FirstOrDefault(i => i.ItemId == ItemId.ItemGreatBall)?.Count;
                var ultraBallsCount = allitems.FirstOrDefault(i => i.ItemId == ItemId.ItemUltraBall)?.Count;
                var masterBallsCount = allitems.FirstOrDefault(i => i.ItemId == ItemId.ItemMasterBall)?.Count;
                masterBallsCount =
                    masterBallsCount == null
                        ? 0
                        : masterBallsCount; //return null ATM. need this code to logic check work

                if (pokeBallsCount + greatBallsCount + ultraBallsCount + masterBallsCount <
                    session.LogicSettings.PokeballsToKeepForSnipe && session.CatchBlockTime < DateTime.Now)
                {
                    session.CatchBlockTime = DateTime.Now.AddMinutes(session.LogicSettings.OutOfBallCatchBlockTime);
                    Logger.Write(session.Translation.GetTranslation(TranslationString.CatchPokemonDisable,
                        session.LogicSettings.OutOfBallCatchBlockTime, session.LogicSettings.PokeballsToKeepForSnipe));
                    return;
                }

                if (session.CatchBlockTime > DateTime.Now) return;

                if ((session.LogicSettings.UsePokemonToNotCatchFilter &&
                     session.LogicSettings.PokemonsNotToCatch.Contains(pokemon.PokemonId)))
                {
                    Logger.Write(session.Translation.GetTranslation(TranslationString.PokemonSkipped,
                        session.Translation.GetPokemonTranslation(pokemon.PokemonId)));
                    continue;
                }

                var distance = LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude,
                    session.Client.CurrentLongitude, pokemon.Latitude, pokemon.Longitude);
                await Task.Delay(distance > 100 ? 500 : 100, cancellationToken);

                //to avoid duplicated encounter when snipe priority pokemon

                if (encounter == null || encounter.Status != EncounterResponse.Types.Status.EncounterSuccess)
                {
                    LocationUtils.UpdatePlayerLocationWithAltitude(session,
                        new GeoCoordinate(pokemon.Latitude, pokemon.Longitude, session.Client.CurrentAltitude), 0); // Set speed to 0 for random speed.
                    encounter =
                        await session.Client.Encounter.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnPointId);
                }

                if (encounter.Status == EncounterResponse.Types.Status.EncounterSuccess &&
                    session.LogicSettings.CatchPokemon)
                {
                    // Catch the Pokemon
                    await CatchPokemonTask.Execute(session, cancellationToken, encounter, pokemon,
                        currentFortData: null, sessionAllowTransfer: sessionAllowTransfer);
                }
                else if (encounter.Status == EncounterResponse.Types.Status.PokemonInventoryFull)
                {
                    if (session.LogicSettings.TransferDuplicatePokemon || session.LogicSettings.TransferWeakPokemon)
                    {
                        session.EventDispatcher.Send(new WarnEvent
                        {
                            Message = session.Translation.GetTranslation(TranslationString.InvFullTransferring)
                        });
                        if (session.LogicSettings.TransferDuplicatePokemon)
                            await TransferDuplicatePokemonTask.Execute(session, cancellationToken);
                        if (session.LogicSettings.TransferWeakPokemon)
                            await TransferWeakPokemonTask.Execute(session, cancellationToken);
                        if (session.LogicSettings.EvolveAllPokemonAboveIv ||
                            session.LogicSettings.EvolveAllPokemonWithEnoughCandy ||
                            session.LogicSettings.UseLuckyEggsWhileEvolving ||
                            session.LogicSettings.KeepPokemonsThatCanEvolve)
                        {
                            await EvolvePokemonTask.Execute(session, cancellationToken);
                        }
                    }
                    else
                        session.EventDispatcher.Send(new WarnEvent
                        {
                            Message = session.Translation.GetTranslation(TranslationString.InvFullTransferManually)
                        });
                }
                else
                {
                    session.EventDispatcher.Send(new WarnEvent
                    {
                        Message =
                            session.Translation.GetTranslation(TranslationString.EncounterProblem, encounter.Status)
                    });
                }
                encounter = null;
                // If pokemon is not last pokemon in list, create delay between catches, else keep moving.
                if (!Equals(pokemons.ElementAtOrDefault(pokemons.Count() - 1), pokemon))
                {
                    await Task.Delay(session.LogicSettings.DelayBetweenPokemonCatch, cancellationToken);
                }
            }
        }

        public static async Task<IOrderedEnumerable<MapPokemon>> GetNearbyPokemons(ISession session)
        {
            var mapObjects = await session.Client.Map.GetMapObjects();
            if (mapObjects == null || mapObjects.MapCells == null) return null;

            var forts = mapObjects.MapCells.SelectMany(p => p.Forts).ToList();
            var nearbyPokemons = mapObjects.MapCells.SelectMany(x => x.NearbyPokemons).ToList();
            
            session.EventDispatcher.Send(new PokeStopListEvent(forts, nearbyPokemons));

            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons)
                .Where(pokemon=>LocationUtils.CalculateDistanceInMeters(pokemon.Latitude, pokemon.Longitude, session.Client.CurrentLatitude, session.Client.CurrentLongitude) <= session.Client.GlobalSettings.MapSettings.EncounterRangeMeters)
                .OrderBy(
                    i =>
                        LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude,
                            session.Client.CurrentLongitude,
                            i.Latitude, i.Longitude));

            return pokemons;
        }
        //add delegate event
        public static event PokemonsEncounterDelegate PokemonEncounterEvent;

        private static void OnPokemonEncounterEvent(List<MapPokemon> pokemons)
        {
            PokemonEncounterEvent?.Invoke(pokemons);
        }
    }
}