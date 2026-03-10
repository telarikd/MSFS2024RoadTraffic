using RoadTraffic.Core.Models;
using RoadTraffic.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoadTraffic.Core
{
    public class TrafficSpawner
    {
        private const double NearRadiusM = 1200.0;
        private const double MarkerRadiusM = 3000.0;
        private const double MinVehicleSpacingM = 10.0;

        private readonly Random _rng = new Random();
        private readonly ILogger _logger;

        public TrafficSpawner(ILogger logger)
        {
            _logger = logger;
        }

        public List<TrafficVehicle> Spawn(
            IReadOnlyList<RoadSegment> roads,
            GeoCoordinate playerPos,
            IReadOnlyList<TrafficVehicle> existingVehicles,
            int highDetailSlots,
            int markerSlots,
            string vehicleTitle)
        {
            var spawned = new List<TrafficVehicle>();
            if (roads == null || roads.Count == 0)
            {
                return spawned;
            }

            var nearbyRoads = roads
                .Where(r => r.DistanceToPoint(playerPos) < 1500.0)
                .ToList();

            if (nearbyRoads.Count == 0)
            {
                nearbyRoads = roads.ToList();
            }

            var shuffled = nearbyRoads
                .OrderBy(_ => _rng.Next())
                .ToList();

            int attempts = Math.Min(400, shuffled.Count * 3);

            for (int i = 0; i < attempts && (highDetailSlots > 0 || markerSlots > 0); i++)
            {
                var road = shuffled[i % shuffled.Count];
                if (road.Nodes.Count < 2 || road.LengthMeters < 40.0)
                {
                    continue;
                }

                int edgeIndex = _rng.Next(road.Nodes.Count - 1);
                var start = road.Nodes[edgeIndex];
                var end = road.Nodes[edgeIndex + 1];
                double t = _rng.NextDouble();

                double lat = start.Latitude + (end.Latitude - start.Latitude) * t;
                double lon = start.Longitude + (end.Longitude - start.Longitude) * t;
                double heading = ComputeHeadingDegrees(start, end);
                if (_rng.NextDouble() < 0.5)
                {
                    heading = (heading + 180.0) % 360.0;
                }

                var candidatePos = new GeoCoordinate(lat, lon);
                double distance = playerPos.DistanceTo(candidatePos);
                _logger.Info($"[SPAWN DEBUG] dist={distance:F0}m road={road.OsmId}");

                if (distance > MarkerRadiusM)
                {
                    continue;
                }

                if (!HasMinimumSpacing(candidatePos, existingVehicles, spawned))
                {
                    continue;
                }

                if (distance <= NearRadiusM && highDetailSlots > 0)
                {
                    spawned.Add(new TrafficVehicle(road.OsmId, lat, lon, heading, isHighDetail: true, isRedMarker: false, vehicleTitle));
                    highDetailSlots--;
                    continue;
                }

                if (distance > NearRadiusM && markerSlots > 0)
                {
                    bool isRed = _rng.NextDouble() < 0.5;
                    spawned.Add(new TrafficVehicle(road.OsmId, lat, lon, heading, isHighDetail: false, isRedMarker: isRed, vehicleTitle));
                    markerSlots--;
                }
            }

            return spawned;
        }

        private static bool HasMinimumSpacing(GeoCoordinate candidatePos, IReadOnlyList<TrafficVehicle> existing, IReadOnlyList<TrafficVehicle> staged)
        {
            foreach (var vehicle in existing)
            {
                if (candidatePos.DistanceTo(vehicle.Position) < MinVehicleSpacingM)
                {
                    return false;
                }
            }

            foreach (var vehicle in staged)
            {
                if (candidatePos.DistanceTo(vehicle.Position) < MinVehicleSpacingM)
                {
                    return false;
                }
            }

            return true;
        }

        private static double ComputeHeadingDegrees(GeoCoordinate start, GeoCoordinate end)
        {
            double deltaLat = end.Latitude - start.Latitude;
            double deltaLon = end.Longitude - start.Longitude;
            double radians = Math.Atan2(deltaLon, deltaLat);
            double degrees = radians * 180.0 / Math.PI;
            return (degrees + 360.0) % 360.0;
        }
    }
}
