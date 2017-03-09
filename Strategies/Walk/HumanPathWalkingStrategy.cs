﻿using System;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Model;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using PokemonGo.RocketAPI;
using System.Device.Location;

namespace PoGo.NecroBot.Logic.Strategies.Walk
{
    class HumanPathWalkingStrategy : BaseWalkStrategy
    {
        private double CurrentWalkingSpeed = 0;

        public HumanPathWalkingStrategy(Client client) : base(client)
        {
        }

        public override string RouteName => "NecroBot GPX";

        public override async Task Walk(IGeoLocation targetLocation,
            Func<Task> functionExecutedWhileWalking, ISession session, CancellationToken cancellationToken,
            double walkSpeed = 0.0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
            var destinaionCoordinate = new GeoCoordinate(targetLocation.Latitude, targetLocation.Longitude);
            //PlayerUpdateResponse result = null;

            if (CurrentWalkingSpeed <= 0)
                CurrentWalkingSpeed = session.LogicSettings.WalkingSpeedInKilometerPerHour;
            if (session.LogicSettings.UseWalkingSpeedVariant && walkSpeed == 0)
                CurrentWalkingSpeed = session.Navigation.VariantRandom(session, CurrentWalkingSpeed);

            var rw = new Random();
            var speedInMetersPerSecond = (walkSpeed > 0 ? walkSpeed : CurrentWalkingSpeed) / 3.6;
            var sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
            LocationUtils.CalculateDistanceInMeters(sourceLocation, destinaionCoordinate);
            var nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, destinaionCoordinate);
            var nextWaypointDistance = speedInMetersPerSecond;
            var waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);
            var requestSendDateTime = DateTime.Now;
            var requestVariantDateTime = DateTime.Now;

            LocationUtils.UpdatePlayerLocationWithAltitude(session, waypoint, (float) speedInMetersPerSecond);

            double SpeedVariantSec = rw.Next(1000, 10000);
            base.DoUpdatePositionEvent(session, waypoint.Latitude, waypoint.Longitude, walkSpeed, CurrentWalkingSpeed);

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
                var millisecondsUntilGetUpdatePlayerLocationResponse =
                    (DateTime.Now - requestSendDateTime).TotalMilliseconds;
                var millisecondsUntilVariant =
                    (DateTime.Now - requestVariantDateTime).TotalMilliseconds;

                sourceLocation = new GeoCoordinate(_client.CurrentLatitude, _client.CurrentLongitude);
                var currentDistanceToTarget = LocationUtils
                    .CalculateDistanceInMeters(sourceLocation, destinaionCoordinate);

                //if (currentDistanceToTarget < 40)
                //{
                //    if (speedInMetersPerSecond > SpeedDownTo)
                //    {
                //        //Logger.Write("We are within 40 meters of the target. Speeding down to 10 km/h to not pass the target.", LogLevel.Info);
                //        speedInMetersPerSecond = SpeedDownTo;
                //    }
                //}

                if (session.LogicSettings.UseWalkingSpeedVariant && walkSpeed == 0)
                {
                    CurrentWalkingSpeed = session.Navigation.VariantRandom(session, CurrentWalkingSpeed);
                    speedInMetersPerSecond = CurrentWalkingSpeed / 3.6;
                }

                nextWaypointDistance = Math.Min(currentDistanceToTarget,
                    millisecondsUntilGetUpdatePlayerLocationResponse / 1000 * speedInMetersPerSecond);
                nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, destinaionCoordinate);
                waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

                requestSendDateTime = DateTime.Now;
                LocationUtils.UpdatePlayerLocationWithAltitude(session, waypoint, (float) speedInMetersPerSecond);

                base.DoUpdatePositionEvent(session, waypoint.Latitude, waypoint.Longitude, CurrentWalkingSpeed);

                if (functionExecutedWhileWalking != null)
                    await functionExecutedWhileWalking(); // look for pokemon & hit stops
            } while (LocationUtils.CalculateDistanceInMeters(sourceLocation, destinaionCoordinate) >= 2);
        }

        public override double CalculateDistance(double sourceLat, double sourceLng, double destinationLat,
            double destinationLng, ISession session = null)
        {
            return LocationUtils.CalculateDistanceInMeters(sourceLat, sourceLng, destinationLat, destinationLng);
        }
    }
}