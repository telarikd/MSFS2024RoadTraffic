using System.Collections.Generic;

namespace MSFSTraffic.Models
{
    /// <summary>
    /// Profil provozniho chovani pro jeden typ silnice.
    /// </summary>
    public class TrafficProfile
    {
        /// <summary>Zakladni rychlost v km/h.</summary>
        public double SpeedKmh { get; set; }

        /// <summary>Variace rychlosti ±X km/h (vozidla jezdí SpeedKmh ± tato hodnota).</summary>
        public double SpeedVariationKmh { get; set; }

        /// <summary>
        /// Ocekavany pocet vozidel na SEGMENTU pri UserDensityMultiplier=1.0.
        /// Hodnoty >= 1.0: vzdy spawni tolik vozidel.
        /// Hodnoty 0-1: pravdepodobnost spawnu jednoho vozidla (deterministicka podle OsmId).
        /// </summary>
        public double DensityPerSegment { get; set; }

        /// <summary>
        /// Vrati slovnik profilu pro vsechny typy silnic.
        /// </summary>
        public static Dictionary<RoadType, TrafficProfile> CreateDefaults()
        {
            return new Dictionary<RoadType, TrafficProfile>
            {
                // DensityPerSegment: pri multiplier 1.0
                //   >= 1.0 = vzdy tolik vozidel na segmentu
                //   0-1.0 = pravdepodobnost spawnu 1 vozidla (deterministicka)
                { RoadType.Motorway,     new TrafficProfile { SpeedKmh = 140, SpeedVariationKmh = 25, DensityPerSegment = 4.5 } },
                { RoadType.Trunk,        new TrafficProfile { SpeedKmh =  90, SpeedVariationKmh = 10, DensityPerSegment = 3.2 } },
                { RoadType.Primary,      new TrafficProfile { SpeedKmh =  70, SpeedVariationKmh = 10, DensityPerSegment = 1.7 } },
                { RoadType.Secondary,    new TrafficProfile { SpeedKmh =  60, SpeedVariationKmh = 10, DensityPerSegment = 0.8 } },
                { RoadType.Tertiary,     new TrafficProfile { SpeedKmh =  50, SpeedVariationKmh =  8, DensityPerSegment = 0.6 } },
                { RoadType.Residential,  new TrafficProfile { SpeedKmh =  30, SpeedVariationKmh =  5, DensityPerSegment = 0.4 } },
                { RoadType.Unclassified, new TrafficProfile { SpeedKmh =  30, SpeedVariationKmh =  5, DensityPerSegment = 0.3 } },
                { RoadType.Track,        new TrafficProfile { SpeedKmh =  20, SpeedVariationKmh =  5, DensityPerSegment = 0.1 } },
                { RoadType.Unknown,      new TrafficProfile { SpeedKmh =  30, SpeedVariationKmh =  5, DensityPerSegment = 0.2 } },
            };
        }
    }
}
