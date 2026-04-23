using System.Collections.Generic;
using System.Threading.Tasks;

namespace MSFSTraffic.Roads
{
    using MSFSTraffic.Models;

    public interface IRoadProvider
    {
        Task<List<RoadSegment>> GetRoadsAroundAsync(GeoCoordinate center, double radiusMeters = 5000);
        void ClearDistantTiles(GeoCoordinate center, double maxDistanceDeg = 0.2);
    }
}
