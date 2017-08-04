#region using directives

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.DataDumper;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class DisplayPokemonStatsTask
    {
        public static List<ulong> PokemonId = new List<ulong>();

        public static List<ulong> PokemonIdcp = new List<ulong>();

        public static async Task Execute(ISession session)
        {
            var highestsPokemonCp = await
                session.Inventory.GetHighestsCp(session.LogicSettings.AmountOfPokemonToDisplayOnStart).ConfigureAwait(false);

            var pokemonPairedWithStatsCp =
                highestsPokemonCp.Select(
                        pokemon =>
                            Tuple.Create(
                                pokemon,
                                PokemonInfo.CalculateMaxCp(pokemon.PokemonId),
                                PokemonInfo.CalculatePokemonPerfection(pokemon),
                                PokemonInfo.GetLevel(pokemon),
                                PokemonInfo.GetPokemonMove1(pokemon),
                                PokemonInfo.GetPokemonMove2(pokemon),
                                PokemonInfo.GetCandy(session, pokemon).Result
                            )
                    )
                    .ToList();

            var highestsPokemonPerfect = await
                session.Inventory.GetHighestsPerfect(session.LogicSettings.AmountOfPokemonToDisplayOnStart).ConfigureAwait(false);

            var pokemonPairedWithStatsIv = 
                highestsPokemonPerfect.Select(
                        pokemon =>
                            Tuple.Create(
                                pokemon,
                                PokemonInfo.CalculateMaxCp(pokemon.PokemonId),
                                PokemonInfo.CalculatePokemonPerfection(pokemon),
                                PokemonInfo.GetLevel(pokemon),
                                PokemonInfo.GetPokemonMove1(pokemon),
                                PokemonInfo.GetPokemonMove2(pokemon),
                                PokemonInfo.GetCandy(session, pokemon).Result
                            )
                    )
                    .ToList();

            session.EventDispatcher.Send(
                new DisplayHighestsPokemonEvent
                {
                    SortedBy = "CP",
                    PokemonList = pokemonPairedWithStatsCp
                });

            session.EventDispatcher.Send(
                new DisplayHighestsPokemonEvent
                {
                    SortedBy = "IV",
                    PokemonList = pokemonPairedWithStatsIv
                });

            var allPokemonInBag = session.LogicSettings.PrioritizeIvOverCp
                ? await session.Inventory.GetHighestsPerfect(1000).ConfigureAwait(false)
                : await session.Inventory.GetHighestsCp(1000).ConfigureAwait(false);
            if (session.LogicSettings.DumpPokemonStats)
            {
                const string dumpFileName = "PokeBagStats";
                try
                {
                    Dumper.ClearDumpFile(session, dumpFileName);

                    string[] data =
                    {
                        "Pokemon",
                        "Nickname",
                        "Slashed",
                        "Level",
                        "CP",
                        "IV",
                        "Candies",
                        "Power Ups",
                        "Favorite",
                        "Stamina",
                        "Stamina Max",
                        "Move1",
                        "Move2",
                        "Owner Name",
                        "Origin",
                        "Height(M)",
                        "Weight(KG)",
                        "Attack",
                        "Defense",
                        "Stamina",
                        "CP Multi",
                        "Gyms Attacked",
                        "Gyms Defended",
                        "Creationtimems",
                        "Additional CP Multi"
                    };
                    Dumper.Dump(session, data, dumpFileName);

                    // set culture to OS default
                    CultureInfo prevCulture = Thread.CurrentThread.CurrentCulture;
                    CultureInfo culture = CultureInfo.CurrentUICulture;
                    Thread.CurrentThread.CurrentCulture = culture;

                    foreach (var pokemon in allPokemonInBag)
                    {
                        string[] pokemonData =
                        {
                            session.Translation.GetPokemonTranslation(pokemon.PokemonId),
                            pokemon.Nickname,
                            pokemon.IsBad.ToString(),
                            PokemonInfo.GetLevel(pokemon).ToString(),
                            pokemon.Cp.ToString(),
                            PokemonInfo.CalculatePokemonPerfection(pokemon).ToString(),
                            PokemonInfo.GetCandy(session, pokemon).ToString(),
                            pokemon.NumUpgrades.ToString(),
                            pokemon.Favorite.ToString(),
                            pokemon.Stamina.ToString(),
                            pokemon.StaminaMax.ToString(),
                            pokemon.Move1.ToString(),
                            pokemon.Move2.ToString(),
                            pokemon.OwnerName,
                            pokemon.Origin.ToString(),
                            pokemon.HeightM.ToString(),
                            pokemon.WeightKg.ToString(),
                            pokemon.IndividualAttack.ToString(),
                            pokemon.IndividualDefense.ToString(),
                            pokemon.IndividualStamina.ToString(),
                            pokemon.CpMultiplier.ToString(),
                            pokemon.BattlesAttacked.ToString(),
                            pokemon.BattlesDefended.ToString(),
                            pokemon.CreationTimeMs.ToString(),
                            pokemon.AdditionalCpMultiplier.ToString()
                        };
                        Dumper.Dump(session, pokemonData, dumpFileName);
                    }

                    // restore culture
                    Thread.CurrentThread.CurrentCulture = prevCulture;
                }
                catch (IOException)
                {
                    session.EventDispatcher.Send(
                        new ErrorEvent {Message = $"Could not write {dumpFileName} dump file."}
                    );
                }
            }
        }
    }
}


