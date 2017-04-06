using System;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Model;
using PoGo.NecroBot.Logic.State;

namespace PoGo.NecroBot.Logic.Strategies.Walk
{
    public interface IWalkStrategy
    {
        string RouteName { get; }
        event UpdatePositionDelegate UpdatePositionEvent;

        Task Walk(IGeoLocation destinationLocation, Func<Task> functionExecutedWhileWalking,
            ISession session, CancellationToken cancellationToken, double customWalkingSpeed = 0.0);

        double CalculateDistance(double sourceLat, double sourceLng, double destinationLat, double destinationLng,
            ISession session = null);
    }
}