#region using directives

using GeoCoordinatePortable;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Interfaces.Configuration;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.Model;
using PoGo.NecroBot.Logic.Model.Settings;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Strategies.Walk;
using PoGo.NecroBot.Logic.Utils;
using PokemonGo.RocketAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace PoGo.NecroBot.Logic
{
    public delegate void UpdatePositionDelegate(ISession session, double lat, double lng, double speed);
    public delegate void GetRouteDelegate(List<GeoCoordinate> points);

    public class Navigation
    {
        public IWalkStrategy WalkStrategy { get; set; }
        private readonly Client _client;
        public GlobalSettings settings { get; set; }
        private Random WalkingRandom = new Random();
        private List<IWalkStrategy> WalkStrategyQueue { get; set; }
        public Dictionary<Type, DateTime> WalkStrategyBlackList = new Dictionary<Type, DateTime>();
        public ILogicSettings _logicSettings;

        public Navigation(Client client, ILogicSettings logicSettings)
        {
            _client = client;

            InitializeWalkStrategies(logicSettings);
            WalkStrategy = GetStrategy(logicSettings);
        }

        public double VariantRandom(ISession session, double currentSpeed)
        {
            if (WalkingRandom.Next(1, 10) > 5)
            {
                if (WalkingRandom.Next(1, 10) > 5)
                {
                    var randomicSpeed = currentSpeed;
                    var max = session.LogicSettings.WalkingSpeedInKilometerPerHour +
                              session.LogicSettings.WalkingSpeedVariant;
                    randomicSpeed += WalkingRandom.NextDouble() * (0.02 - 0.001) + 0.001;

                    if (randomicSpeed > max)
                        randomicSpeed = max;

                    if (Math.Round(randomicSpeed, 2) != Math.Round(currentSpeed, 2))
                    {
                        session.EventDispatcher.Send(new HumanWalkingEvent
                        {
                            OldWalkingSpeed = currentSpeed,
                            CurrentWalkingSpeed = randomicSpeed
                        });
                    }
                    return randomicSpeed;
                }
                else
                {
                    var randomicSpeed = currentSpeed;
                    var min = session.LogicSettings.WalkingSpeedInKilometerPerHour -
                              session.LogicSettings.WalkingSpeedVariant;
                    randomicSpeed -= WalkingRandom.NextDouble() * (0.02 - 0.001) + 0.001;

                    if (randomicSpeed < min)
                        randomicSpeed = min;

                    if (Math.Round(randomicSpeed, 2) != Math.Round(currentSpeed, 2))
                    {
                        session.EventDispatcher.Send(new HumanWalkingEvent
                        {
                            OldWalkingSpeed = currentSpeed,
                            CurrentWalkingSpeed = randomicSpeed
                        });
                    }
                    return randomicSpeed;
                }
            }
            return currentSpeed;
        }

        public async Task Move(IGeoLocation targetLocation,
            Func<Task> functionExecutedWhileWalking,
            ISession session,
            CancellationToken cancellationToken, double customWalkingSpeed = 0.0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();

            // If the stretegies become bigger, create a factory for easy management

            //Logging.Logger.Write($"Navigation - Walking speed {customWalkingSpeed}");

            //Maybe add auto Google/Yours/Mapzen walk here???
            var distance = LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                targetLocation.Latitude, targetLocation.Longitude);

            bool _GoogleWalk = session.LogicSettings.UseGoogleWalk; // == false ? false : true;
            string _GoogleAPI = session.LogicSettings.GoogleApiKey; // == "" ? null : session.LogicSettings.GoogleApiKey;
            bool _MapZenWalk = session.LogicSettings.UseMapzenWalk; // == false ? false : true;
            string _MapZenAPI = session.LogicSettings.MapzenTurnByTurnApiKey; // == "" ? null : session.LogicSettings.GoogleApiKey;
            bool _YoursWalk = session.LogicSettings.UseYoursWalk;

            settings = new GlobalSettings();

            if (distance >= 100)
            {
                if (_MapZenWalk == false && _MapZenAPI != "")
                {
                    Logger.Write($"Distance to travel is > 100m, switching to 'MapzenWalk'", LogLevel.Info, ConsoleColor.DarkYellow);
                    settings.YoursWalkConfig.UseYoursWalk = false;
                    settings.MapzenWalkConfig.UseMapzenWalk = true;
                    //session.LogicSettings.UseYoursWalk = false;
                    //session.LogicSettings.UseMapzenWalk = true;
                }
                if (_GoogleWalk == false && _GoogleAPI != "")
                {
                    Logger.Write($"Distance to travel is > 100m, switching to 'GoogleWalk'", LogLevel.Info, ConsoleColor.DarkYellow);
                    settings.YoursWalkConfig.UseYoursWalk = false;
                    settings.GoogleWalkConfig.UseGoogleWalk = true;
                    //session.LogicSettings.UseYoursWalk = false;
                    //session.LogicSettings.UseGoogleWalk = true;
                }
            }
            else
            {
                if (_GoogleWalk || _MapZenWalk)
                {
                    Logger.Write($"Distance to travel is < 100m, switching to 'YoursWalk'", LogLevel.Info, ConsoleColor.DarkYellow);
                    settings.YoursWalkConfig.UseYoursWalk = true;
                    settings.GoogleWalkConfig.UseGoogleWalk = false;
                    settings.MapzenWalkConfig.UseMapzenWalk = false;
                    //session.LogicSettings.UseYoursWalk = true;
                    //session.LogicSettings.UseGoogleWalk = false;
                    //session.LogicSettings.UseMapzenWalk = false;
                }
            }
            InitializeWalkStrategies(session.LogicSettings);
            WalkStrategy = GetStrategy(session.LogicSettings);

            await WalkStrategy.Walk(targetLocation, functionExecutedWhileWalking, session, cancellationToken, customWalkingSpeed).ConfigureAwait(false);

            settings.YoursWalkConfig.UseYoursWalk = _YoursWalk;
            settings.GoogleWalkConfig.UseGoogleWalk = _GoogleWalk;
            settings.MapzenWalkConfig.UseMapzenWalk = _MapZenWalk;
            //session.LogicSettings.UseYoursWalk = _YoursWalk;
            //session.LogicSettings.UseGoogleWalk = _GoogleWalk;
            //session.LogicSettings.UseMapzenWalk = _MapZenWalk;

            InitializeWalkStrategies(session.LogicSettings);
            WalkStrategy = GetStrategy(session.LogicSettings);
        }

        private void InitializeWalkStrategies(ILogicSettings logicSettings)
        {
            WalkStrategyQueue = new List<IWalkStrategy>();

            // Maybe change configuration for a Navigation Type.
            if (logicSettings.DisableHumanWalking)
            {
                WalkStrategyQueue.Add(new FlyStrategy(_client));
            }

            if (logicSettings.UseGpxPathing)
            {
                WalkStrategyQueue.Add(new HumanPathWalkingStrategy(_client));
            }

            if (logicSettings.UseGoogleWalk)
            {
                WalkStrategyQueue.Add(new GoogleStrategy(_client));
            }

            if (logicSettings.UseMapzenWalk)
            {
                WalkStrategyQueue.Add(new MapzenNavigationStrategy(_client));
            }

            if (logicSettings.UseYoursWalk)
            {
                WalkStrategyQueue.Add(new YoursNavigationStrategy(_client));
            }

            WalkStrategyQueue.Add(new HumanStrategy(_client));
        }

        public bool IsWalkingStrategyBlacklisted(Type strategy)
        {
            if (!WalkStrategyBlackList.ContainsKey(strategy))
                return false;

            DateTime now = DateTime.Now;
            DateTime blacklistExpiresAt = WalkStrategyBlackList[strategy];
            if (blacklistExpiresAt < now)
            {
                // Blacklist expired
                WalkStrategyBlackList.Remove(strategy);
                return false;
            }
            else
            {
                return true;
            }
        }

        public void BlacklistStrategy(Type strategy)
        {
            // Black list for 1 hour.
            WalkStrategyBlackList[strategy] = DateTime.Now.AddHours(1);
        }

        public IWalkStrategy GetStrategy(ILogicSettings logicSettings)
        {
            return WalkStrategyQueue.First(q => !IsWalkingStrategyBlacklisted(q.GetType()));
        }
    }
}
