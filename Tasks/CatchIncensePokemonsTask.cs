﻿#region using directives

using System;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using POGOProtos.Map.Pokemon;
using POGOProtos.Networking.Responses;
using System.Collections.Generic;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    //add delegate
    public delegate void PokemonsEncounterDelegate(List<MapPokemon> pokemons);

    public static class CatchIncensePokemonsTask
    {
        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.LogicSettings.CatchPokemon || session.CatchBlockTime > DateTime.Now) return;

            Logger.Write(
                session.Translation.GetTranslation(TranslationString.LookingForIncensePokemon),
                LogLevel.Debug
            );

            var incensePokemon = await session.Client.Map.GetIncensePokemons();

            if (incensePokemon.Result == GetIncensePokemonResponse.Types.Result.IncenseEncounterAvailable)
            {
                var pokemon = new MapPokemon
                {
                    EncounterId = incensePokemon.EncounterId,
                    ExpirationTimestampMs = incensePokemon.DisappearTimestampMs,
                    Latitude = incensePokemon.Latitude,
                    Longitude = incensePokemon.Longitude,
                    PokemonId = incensePokemon.PokemonId,
                    SpawnPointId = incensePokemon.EncounterLocation
                };

                //add delegate function
                OnPokemonEncounterEvent(new List<MapPokemon> { pokemon });

                if (session.Cache.Get(incensePokemon.EncounterId.ToString()) != null)
                    return; //pokemon been ignore before

                if ((session.LogicSettings.UsePokemonToNotCatchFilter && session.LogicSettings.PokemonsNotToCatch.Contains(pokemon.PokemonId)))
                {
                    Logger.Write(session.Translation.GetTranslation(TranslationString.PokemonIgnoreFilter,
                        session.Translation.GetPokemonTranslation(pokemon.PokemonId)));
                }
                else
                {
                    var distance = LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude,
                        session.Client.CurrentLongitude, pokemon.Latitude, pokemon.Longitude);
                    await Task.Delay(distance > 100 ? 500 : 100, cancellationToken);

                    var encounter =
                        await
                            session.Client.Encounter.EncounterIncensePokemon((ulong) pokemon.EncounterId,
                                pokemon.SpawnPointId);

                    if (encounter.Result == IncenseEncounterResponse.Types.Result.IncenseEncounterSuccess
                        && session.LogicSettings.CatchPokemon)
                    {
                        //await CatchPokemonTask.Execute(session, cancellationToken, encounter, pokemon);
                        await CatchPokemonTask.Execute(session, cancellationToken, encounter, pokemon,
                            currentFortData: null, sessionAllowTransfer: true);
                    }
                    else if (encounter.Result == IncenseEncounterResponse.Types.Result.PokemonInventoryFull)
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
                                session.Translation.GetTranslation(TranslationString.EncounterProblem, encounter.Result)
                        });
                    }
                }
            }
        }
        //add delegate event
        public static event PokemonsEncounterDelegate PokemonEncounterEvent;

        private static void OnPokemonEncounterEvent(List<MapPokemon> pokemons)
        {
            PokemonEncounterEvent?.Invoke(pokemons);
        }
    }
}