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

        /// <summary>Pocet vozidel na kilometr silnice (pri plne hustote).</summary>
        public double DensityPerKm { get; set; }

        /// <summary>
        /// Vrati slovnik profilu pro vsechny typy silnic.
        /// </summary>
        public static Dictionary<RoadType, TrafficProfile> CreateDefaults()
        {
            return new Dictionary<RoadType, TrafficProfile>
            {
                { RoadType.Motorway,     new TrafficProfile { SpeedKmh = 120, SpeedVariationKmh = 15, DensityPerKm = 0.8 } },
                { RoadType.Trunk,        new TrafficProfile { SpeedKmh =  90, SpeedVariationKmh = 10, DensityPerKm = 0.6 } },
                { RoadType.Primary,      new TrafficProfile { SpeedKmh =  70, SpeedVariationKmh = 10, DensityPerKm = 0.5 } },
                { RoadType.Secondary,    new TrafficProfile { SpeedKmh =  60, SpeedVariationKmh = 10, DensityPerKm = 0.4 } },
                { RoadType.Tertiary,     new TrafficProfile { SpeedKmh =  50, SpeedVariationKmh =  8, DensityPerKm = 0.3 } },
                { RoadType.Residential,  new TrafficProfile { SpeedKmh =  30, SpeedVariationKmh =  5, DensityPerKm = 0.2 } },
                { RoadType.Unclassified, new TrafficProfile { SpeedKmh =  30, SpeedVariationKmh =  5, DensityPerKm = 0.15 } },
                { RoadType.Track,        new TrafficProfile { SpeedKmh =  20, SpeedVariationKmh =  5, DensityPerKm = 0.05 } },
                { RoadType.Unknown,      new TrafficProfile { SpeedKmh =  30, SpeedVariationKmh =  5, DensityPerKm = 0.1  } },
            };
        }
    }
}
