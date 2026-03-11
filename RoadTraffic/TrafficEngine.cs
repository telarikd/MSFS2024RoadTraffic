using System;
using System.Collections.Generic;

namespace MSFSTraffic.Engine
{
    /// <summary>
    /// Pohyblivá světelná vrstva — FlareEffect SimObjects kolem hráče.
    /// Nezávisí na silnicích ani TrafficManager. Čistě vizuální iluze provozu.
    ///
    /// Výkonnostní pravidla:
    ///   - žádný LINQ v update loop
    ///   - žádné alokace v update loop (pre-alokovaný List)
    ///   - max 5 nových SpawnRequestů za tick (ochrana SimConnect fronty)
    ///   - SetDataOnSimObject se volá jen každé PosUpdateEvery ticků
    /// </summary>
    public class TrafficEngine
    {
        // ── Limity ──────────────────────────────────────────────────────────
        private const double SpawnRadiusM   = 3_000;   // nové auto se spawne v tomto okruhu
        private const double DespawnRadiusM = 5_000;   // auto se despawne za touto hranicí
        private const float  SpeedMinMs     = 15f;     // minimální rychlost m/s
        private const float  SpeedMaxMs     = 30f;     // maximální rychlost m/s
        private const int    SpawnPerTick   = 5;       // max nových aut za jeden tick
        private const int    PosUpdateEvery = 3;       // SetDataOnSimObject každé N ticků
        private const double MetersPerDeg   = 111_320.0;

        // ── Stav ────────────────────────────────────────────────────────────
        private readonly List<TrafficCar> _cars = new List<TrafficCar>(800);
        private readonly Random _rng = new Random();
        private int _tick;
        private int _spawnedCount;   // počet aut s IsSpawned==true (potvrzeno SimConnectem)

        // ── Konfigurace (slider) ─────────────────────────────────────────────
        public int MaxCars { get; set; } = 150;

        // ── Events (zapojuje MainWindow) ────────────────────────────────────
        public event Action<TrafficCar> OnSpawnRequested;
        public event Action<TrafficCar> OnDespawnRequested;
        public event Action<TrafficCar> OnPositionUpdated;

        /// <summary>Celkový počet aut v listu (spawned + čekající na potvrzení).</summary>
        public int ActiveCarCount => _cars.Count;

        /// <summary>
        /// Počet flare objektů skutečně živých v simulátoru (IsSpawned == true).
        /// Toto číslo zobrazuje UI counter — umožňuje debugovat failed spawny.
        /// </summary>
        public int ActiveCars => _spawnedCount;

        // ════════════════════════════════════════════════════════════════════
        //  HLAVNÍ UPDATE — voláno z MainWindow.OnUpdateTick
        // ════════════════════════════════════════════════════════════════════

        public void Update(double playerLat, double playerLon, double deltaTime)
        {
            _tick++;

            TrimExcessCars();                           // slider šel dolů → despawn přebytku
            MoveCars(deltaTime);                        // pohyb + throttled position broadcast
            DespawnDistantCars(playerLat, playerLon);   // auta mimo dosah
            SpawnCars(playerLat, playerLon);            // doplnění do MaxCars
        }

        // ════════════════════════════════════════════════════════════════════
        //  POHYB
        // ════════════════════════════════════════════════════════════════════

        private void MoveCars(double deltaTime)
        {
            bool sendPos = (_tick % PosUpdateEvery == 0);

            for (int i = 0; i < _cars.Count; i++)
            {
                TrafficCar car = _cars[i];

                double headingRad = car.Heading * (Math.PI / 180.0);
                double distM      = car.Speed * deltaTime;
                double cosLat     = Math.Cos(car.Lat * (Math.PI / 180.0));

                car.Lat += Math.Cos(headingRad) * distM / MetersPerDeg;
                car.Lon += Math.Sin(headingRad) * distM / (MetersPerDeg * cosLat);

                if (car.IsSpawned && sendPos)
                    OnPositionUpdated?.Invoke(car);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  DESPAWN
        // ════════════════════════════════════════════════════════════════════

        private void DespawnDistantCars(double playerLat, double playerLon)
        {
            for (int i = _cars.Count - 1; i >= 0; i--)
            {
                if (DistM(playerLat, playerLon, _cars[i].Lat, _cars[i].Lon) > DespawnRadiusM)
                {
                    if (_cars[i].IsSpawned)
                    {
                        _spawnedCount--;
                        OnDespawnRequested?.Invoke(_cars[i]);
                    }
                    _cars.RemoveAt(i);
                }
            }
        }

        /// <summary>Pokud uživatel snížil slider, okamžitě odstraní přebytečná auta.</summary>
        private void TrimExcessCars()
        {
            for (int i = _cars.Count - 1; i >= MaxCars; i--)
            {
                if (_cars[i].IsSpawned)
                {
                    _spawnedCount--;
                    OnDespawnRequested?.Invoke(_cars[i]);
                }
                _cars.RemoveAt(i);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SPAWN
        // ════════════════════════════════════════════════════════════════════

        private void SpawnCars(double playerLat, double playerLon)
        {
            int toSpawn = Math.Min(MaxCars - _cars.Count, SpawnPerTick);
            if (toSpawn <= 0) return;

            double cosLat = Math.Cos(playerLat * (Math.PI / 180.0));

            for (int i = 0; i < toSpawn; i++)
            {
                double angle = _rng.NextDouble() * (2.0 * Math.PI);
                double dist  = 200.0 + _rng.NextDouble() * (SpawnRadiusM - 200.0);

                var car = new TrafficCar
                {
                    Lat     = playerLat + Math.Cos(angle) * dist / MetersPerDeg,
                    Lon     = playerLon + Math.Sin(angle) * dist / (MetersPerDeg * cosLat),
                    Heading = _rng.NextDouble() * 360.0,
                    Speed   = SpeedMinMs + (float)(_rng.NextDouble() * (SpeedMaxMs - SpeedMinMs))
                };

                _cars.Add(car);
                OnSpawnRequested?.Invoke(car);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SIMCONNECT CALLBACKS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Voláno z MainWindow.OnRecvAssignedObjectId po úspěšném spawnu.
        /// Doplní ObjectId a příznak IsSpawned na auto v listu.
        /// </summary>
        public void ConfirmSpawn(uint requestId, uint objectId)
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                if (_cars[i].RequestId == requestId)
                {
                    _cars[i].ObjectId  = objectId;
                    _cars[i].IsSpawned = true;
                    _spawnedCount++;
                    return;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  CLEANUP
        // ════════════════════════════════════════════════════════════════════

        public void RemoveAll()
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                if (_cars[i].IsSpawned)
                    OnDespawnRequested?.Invoke(_cars[i]);
            }
            _cars.Clear();
            _spawnedCount = 0;
        }

        // ════════════════════════════════════════════════════════════════════
        //  HELPER — Haversine vzdálenost
        // ════════════════════════════════════════════════════════════════════

        private static double DistM(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6_371_000.0;
            double dLat = (lat2 - lat1) * (Math.PI / 180.0);
            double dLon = (lon2 - lon1) * (Math.PI / 180.0);
            double a    = Math.Sin(dLat * 0.5) * Math.Sin(dLat * 0.5)
                        + Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0))
                        * Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);
            return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }
    }
}
