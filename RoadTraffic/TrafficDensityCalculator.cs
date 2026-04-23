using System;
using System.Collections.Generic;

namespace MSFSTraffic.Models
{
    /// <summary>
    /// Vypocitava idealni pocet vozidel na silnicnim segmentu
    /// na zaklade casu, dne a uzivatelske hustoty.
    /// </summary>
    public class TrafficDensityCalculator : ITrafficDensityCalculator
    {
        private readonly Dictionary<RoadType, TrafficProfile> _profiles;

        /// <summary>Uzivatelsky nasobitel hustoty (0.0 = zadna auta, 2.0 = dvojnasobek).</summary>
        public double UserDensityMultiplier { get; set; } = 1.0;

        public TrafficDensityCalculator()
        {
            _profiles = TrafficProfile.CreateDefaults();
        }

        /// <summary>
        /// Vrati idealni pocet vozidel na danem segmentu pro aktualni cas a den.
        /// </summary>
        /// <param name="road">Silnicni segment.</param>
        /// <param name="simTimeHours">Cas v MSFS (0.0–24.0).</param>
        /// <param name="isWeekend">Je vikend?</param>
        public int CalculateVehicleCount(RoadSegment road, double simTimeHours, bool isWeekend)
        {
            if (!_profiles.TryGetValue(road.RoadType, out TrafficProfile profile))
                return 0;

            if (road.LengthMeters < 30) return 0; // prilis kratky segment, skip

            double timeFactor = GetTimeFactor(simTimeHours);
            double weekendFactor = GetWeekendFactor(road.RoadType, isWeekend);

            // DensityPerSegment = ocekavany pocet vozidel na segmentu pri multiplier 1.0
            // Nenasobime delkou — OSM segmenty jsou prilis kratke (~50-300m)
            double raw = profile.DensityPerSegment * timeFactor * weekendFactor * UserDensityMultiplier;

            if (raw <= 0) return 0;

            // Pro hodnoty >= 1.0: return rounded count
            if (raw >= 1.0) return (int)Math.Round(raw);

            // Pro hodnoty < 1.0: deterministicka pravdepodobnost podle OsmId
            // Stejny segment vzdy spawnuje / nespawnuje za stejnych podminek
            int hash = (int)(Math.Abs(road.OsmId) % 1000);
            return hash < (int)(raw * 1000) ? 1 : 0;
        }

        // ── Helpers ──

        private static double GetTimeFactor(double hours)
        {
            // Noc (22–6): 0.3x
            if (hours >= 22 || hours < 6)
                return 0.3;

            // Rush hour rano (7–9): 1.5x
            if (hours >= 7 && hours < 9)
                return 1.5;

            // Rush hour odpoledne (16–19): 1.5x
            if (hours >= 16 && hours < 19)
                return 1.5;

            // Pozni noc / brzke rano (6–7, 19–22): 0.7x
            if ((hours >= 6 && hours < 7) || (hours >= 19 && hours < 22))
                return 0.7;

            // Ostatni (9–16): normalni provoz
            return 1.0;
        }

        private static double GetWeekendFactor(RoadType roadType, bool isWeekend)
        {
            if (!isWeekend) return 1.0;

            // Vikend: mene aut na hlavnich silnicich, vice na rezidencionalnich (vylety)
            switch (roadType)
            {
                case RoadType.Motorway:
                case RoadType.Trunk:
                case RoadType.Primary:
                    return 0.8;   // -20% — mene dojezdu do prace

                case RoadType.Residential:
                    return 1.1;   // +10% — vylety, nakupy

                default:
                    return 1.0;
            }
        }
    }
}
