using System.Collections.Generic;

namespace RoadTraffic.Core.Models
{
    public class TrafficProfile
    {
        public double SpeedKmh { get; set; }

        public double SpeedVariationKmh { get; set; }

        public double DensityPerSegment { get; set; }

        public static Dictionary<RoadType, TrafficProfile> CreateDefaults()
        {
            return new Dictionary<RoadType, TrafficProfile>
            {
                { RoadType.Motorway,     new TrafficProfile { SpeedKmh = 140, SpeedVariationKmh = 25, DensityPerSegment = 4.5 } },
                { RoadType.Trunk,        new TrafficProfile { SpeedKmh = 90, SpeedVariationKmh = 10, DensityPerSegment = 3.2 } },
                { RoadType.Primary,      new TrafficProfile { SpeedKmh = 70, SpeedVariationKmh = 10, DensityPerSegment = 1.7 } },
                { RoadType.Secondary,    new TrafficProfile { SpeedKmh = 60, SpeedVariationKmh = 10, DensityPerSegment = 0.8 } },
                { RoadType.Tertiary,     new TrafficProfile { SpeedKmh = 50, SpeedVariationKmh = 8, DensityPerSegment = 0.6 } },
                { RoadType.Residential,  new TrafficProfile { SpeedKmh = 30, SpeedVariationKmh = 5, DensityPerSegment = 0.4 } },
                { RoadType.Unclassified, new TrafficProfile { SpeedKmh = 30, SpeedVariationKmh = 5, DensityPerSegment = 0.3 } },
                { RoadType.Track,        new TrafficProfile { SpeedKmh = 20, SpeedVariationKmh = 5, DensityPerSegment = 0.1 } },
                { RoadType.Unknown,      new TrafficProfile { SpeedKmh = 30, SpeedVariationKmh = 5, DensityPerSegment = 0.2 } }
            };
        }
    }
}
