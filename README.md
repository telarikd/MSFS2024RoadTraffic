# MSFS2024RoadTraffic

Lightweight realistic road traffic engine for Microsoft Flight Simulator 2024 using SimConnect.

## Overview

MSFS2024RoadTraffic is an experimental traffic simulation engine that generates dynamic road traffic around the aircraft using real road data.

The goal is to provide more realistic ground traffic with minimal performance impact.

## Features (planned)

- Dynamic road traffic spawning
- Traffic density simulation
- Road data from OpenStreetMap / Overpass
- Lightweight vehicle simulation
- SimConnect integration
- Configurable traffic profiles

## Architecture

Core components:

- **TrafficManager** – controls spawning and despawning vehicles
- **TrafficDensityCalculator** – determines traffic density by location and time
- **RoadSegment** – representation of road network segments
- **TrafficVehicle** – simulated vehicle instance
- **GeoCoordinate** – geographic coordinate system

## Project structure
