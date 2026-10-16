# RoadTrafficCar

This package contains the low-poly `RoadTrafficCar` MSFS GroundVehicle used by
the RoadTraffic traffic engine. The model is generated from source with no
external modelling or texture tools.

## Build

Run `build.bat` from this directory. It generates the GLB, refreshes
`layout.json`, and creates `roadtraffic-car.zip`.

## Install and validate

Copy the `RoadTrafficCar` directory to the MSFS 2024 Community directory and
reload packages or restart MSFS. In DevMode SimObject Spawner, search for
`RoadTrafficCar` to confirm the package title is available.

After validation, change the application vehicle title from `HAmphibiusFemale`
to `RoadTrafficCar`. The traffic engine will then use the car without any
change to spawning, routing, or movement logic.

The model uses a ground-level pivot, has a 4.15 m body length, and points along
positive Z. Verify heading in the simulator before making it the runtime
default; model orientation is an asset-level concern, not a second traffic
implementation.
