using System;
using System.Collections.Generic;

namespace RoadTraffic.Core.Models
{
    public class TrafficDensityCalculator
    {
        private readonly Dictionary<RoadType, TrafficProfile> _profiles;

        public TrafficDensityCalculator()
        {
            _profiles = TrafficProfile.CreateDefaults();
        }

        public double UserDensityMultiplier { get; set; } = 1.0;

        public int CalculateVehicleCount(RoadSegment road, double simTimeHours, bool isWeekend)
        {
            if (!_profiles.TryGetValue(road.RoadType, out TrafficProfile profile))
            {
                return 0;
            }

            if (road.LengthMeters < 30)
            {
                return 0;
            }

            double raw = profile.DensityPerSegment * GetTimeFactor(simTimeHours) * GetWeekendFactor(road.RoadType, isWeekend) * UserDensityMultiplier;
            if (raw <= 0)
            {
                return 0;
            }

            if (raw >= 1.0)
            {
                return (int)Math.Round(raw);
            }

            int hash = (int)(Math.Abs(road.OsmId) % 1000);
            return hash < (int)(raw * 1000) ? 1 : 0;
        }

        private static double GetTimeFactor(double hours)
        {
            if (hours >= 22 || hours < 6)
            {
                return 0.3;
            }

            if ((hours >= 7 && hours < 9) || (hours >= 16 && hours < 19))
            {
                return 1.5;
            }

            if ((hours >= 6 && hours < 7) || (hours >= 19 && hours < 22))
            {
                return 0.7;
            }

            return 1.0;
        }

        private static double GetWeekendFactor(RoadType roadType, bool isWeekend)
        {
            if (!isWeekend)
            {
                return 1.0;
            }

            switch (roadType)
            {
                case RoadType.Motorway:
                case RoadType.Trunk:
                case RoadType.Primary:
                    return 0.8;
                case RoadType.Residential:
                    return 1.1;
                default:
                    return 1.0;
            }
        }
    }
}
