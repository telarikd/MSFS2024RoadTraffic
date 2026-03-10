using RoadTraffic.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RoadTraffic.Core
{
    public interface IRoadProvider
    {
        Task<IReadOnlyList<RoadSegment>> GetRoadsAroundAsync(GeoCoordinate center, double radiusMeters = 5000);

        void ClearDistantTiles(GeoCoordinate center, double maxDistanceDeg = 0.2);
    }
}
