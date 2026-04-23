#!/usr/bin/env python3
"""
create_package.py
=================
Builds the RoadTrafficLight MSFS 2024 SimObject package
based on the exact structure of a verified working package.

Output: roadtraffic-rt-light/  +  roadtraffic-rt-light.zip
"""

import os, sys, json, struct, zipfile, time, pathlib

# ── Paths ────────────────────────────────────────────────────────────────────
SCRIPT_DIR   = pathlib.Path(__file__).parent
PKG_NAME     = "roadtraffic-rt-light"
SIMOBJ_NAME  = "RoadTrafficLight"   # MUST match FLARE_EFFECT_TITLE in C#
AUTHOR       = "roadtraffic"
PKG_DIR      = SCRIPT_DIR / PKG_NAME
SIMOBJ_DIR   = PKG_DIR / "SimObjects" / "GroundVehicles" / SIMOBJ_NAME

WIN_EPOCH    = 11_644_473_600

# ── Helpers ───────────────────────────────────────────────────────────────────
def write(path, content):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(content, str):
        path.write_text(content, encoding="utf-8")
    else:
        path.write_bytes(content)

def filetime(p):
    ts = pathlib.Path(p).stat().st_mtime
    return int((ts + WIN_EPOCH) * 10_000_000)

# ═════════════════════════════════════════════════════════════════════════════
#  1. MANIFEST.JSON  — exact fields from working package
# ═════════════════════════════════════════════════════════════════════════════
manifest = {
    "dependencies": [],
    "content_type": "MISC",
    "title": SIMOBJ_NAME,
    "manufacturer": "",
    "creator": AUTHOR,
    "package_version": "0.1.0",
    "minimum_game_version": "1.6.34",
    "minimum_compatibility_version": "6.34.0.169",
    "export_type": "Community",
    "builder": "Microsoft Flight Simulator 2024",
    "package_order_hint": "CUSTOM_SIMOBJECTS",
    "release_notes": {"neutral": {"LastUpdate": "", "OlderHistory": ""}},
    "total_package_size": "0"
}
write(PKG_DIR / "manifest.json", json.dumps(manifest, indent=2))

# ═════════════════════════════════════════════════════════════════════════════
#  2. SIM.CFG  (root + common/config — identical, exact format from working pkg)
# ═════════════════════════════════════════════════════════════════════════════
sim_cfg = f"""[VERSION]
Major=1
Minor=0

[fltsim.0]
title={SIMOBJ_NAME}
model=
texture=

[General]
category=GroundVehicle

[PILOT]
pilot_default_animation = "DRIVING_Car_Froward"
pilot_attach_node = "PILOT_0"

[contact_points]
wheel_radius=0.30
static_pitch=0.0
static_cg_height=0

[DesignSpecs]
max_speed_mph = 120
acceleration_constants = 0.3, 0.4
deceleration_constants = 0.2, 0.4

[WAYPOINT]
FrontRadiusMeters = 2.5
SlowDownOnLastWayPoint = 1
StopDistance = 5
"""
write(SIMOBJ_DIR / "sim.cfg",                  sim_cfg)
write(SIMOBJ_DIR / "common" / "config" / "sim.cfg", sim_cfg)

# ═════════════════════════════════════════════════════════════════════════════
#  3. COMMON/MODEL  — model.cfg + exterior.xml + interior.xml + Helper.gltf
# ═════════════════════════════════════════════════════════════════════════════
model_cfg = """; [model.options] copied from working package
[model.options]
withExterior_showInterior=true
withExterior_showInterior_hideFirstLod=true
withInterior_forceFirstLod=true
withInterior_showExterior=true

[models]
interior = interior.xml
exterior = exterior.xml
"""
write(SIMOBJ_DIR / "common" / "model" / "model.cfg", model_cfg)

exterior_xml_common = """<?xml version="1.0" encoding="utf-8"?>
<ModelInfo>
\t<LODS>
\t\t<LOD ModelFile="Helper.gltf"/>
\t</LODS>
</ModelInfo>
"""
write(SIMOBJ_DIR / "common" / "model" / "exterior.xml", exterior_xml_common)

interior_xml = """<?xml version="1.0" encoding="utf-8"?>
<ModelInfo>
\t<LODS>
\t\t<LOD minSize="5" ModelFile="Helper.gltf"/>
\t\t<LOD minSize="4" ModelFile="Helper.gltf"/>
\t\t<LOD minSize="3" ModelFile="Helper.gltf"/>
\t\t<LOD minSize="2" ModelFile="Helper.gltf"/>
\t\t<LOD minSize="1" ModelFile="Helper.gltf"/>
\t</LODS>
</ModelInfo>
"""
write(SIMOBJ_DIR / "common" / "model" / "interior.xml", interior_xml)

# Helper.gltf — exact copy from working package (minimal ASOBO stub)
helper_gltf = '{"asset":{"generator":"babylon.js glTF exporter for 3dsmax 2022 v1.0.8504.28824","version":"2.0","extensions":{"ASOBO_asset_optimized":{"MajorVersion":4,"MinorVersion":6},"ASOBO_normal_map_convention":{"tangent_space_convention":"DirectX"}}},"nodes":[{"extensions":{"ASOBO_unique_id":{"id":"B_Helper"}},"name":"B_Helper"}],"scene":0,"scenes":[{"nodes":[0]}],"extensionsUsed":["ASOBO_normal_map_convention","ASOBO_unique_id","ASOBO_asset_optimized"]}'
write(SIMOBJ_DIR / "common" / "model" / "Helper.gltf", helper_gltf)

# ═════════════════════════════════════════════════════════════════════════════
#  4. SOUNDAI  — exact copy from working package
# ═════════════════════════════════════════════════════════════════════════════
soundai_xml = """<?xml version="1.0" encoding="utf-8" ?>
<SoundInfo Version="0.1">
  <WwisePackages>
    <SharedPackage Name="Asobo_Ground_Vehicles_Utility_02"/>
  </WwisePackages>
  <EngineSoundPresets>
    <Sound WwiseEvent="Combustion" SharedPackageName="Asobo_Ground_Vehicles_Utility_02" WwiseData="true" EngineIndex="1" FadeOutType="2" FadeOutTime="0.5">
      <WwiseRTPC SimVar="GENERAL ENG COMBUSTION SOUND PERCENT" Units="PERCENT OVER 100" Index="1" RTPCName="SIMVAR_GENERAL_ENG_COMBUSTION_SOUND_PERCENT_CUSTOM"/>
      <WwiseRTPC SimVar="GROUND VELOCITY" Units="KNOTS" Index="1" RTPCName="SIMVAR_GROUND_VELOCITY_CUSTOM"/>
    </Sound>
    <Sound WwiseEvent="Shutdown" WwiseData="true" SharedPackageName="Asobo_Ground_Vehicles_Utility_02" EngineIndex="1"/>
  </EngineSoundPresets>
  <EngineSoundTransitions>
    <Sound WwiseEvent="eng1_combustion_start" WwiseData="true" SharedPackageName="Asobo_Ground_Vehicles_Utility_02" Continuous="false" FadeOutType="2" FadeOutTime="0.2" EngineIndex="1" StateTo="On" StateFrom="Off"/>
  </EngineSoundTransitions>
  <SimVarSounds>
    <Sound WwiseData="true" WwiseEvent="tires_roll" SharedPackageName="Asobo_Ground_Vehicles_Utility_02" SimVar="GROUND VELOCITY" Units="KNOTS" Index="1">
      <WwiseRTPC SimVar="GROUND VELOCITY" Units="KNOTS" Index="1" RTPCName="SIMVAR_GROUND_VELOCITY_CUSTOM"/>
      <Range LowerBound="0.01"/>
    </Sound>
  </SimVarSounds>
</SoundInfo>
"""
write(SIMOBJ_DIR / "common" / "soundai" / "soundai.xml", soundai_xml)

# ═════════════════════════════════════════════════════════════════════════════
#  5. LIVERIES  — exact copy from working package
# ═════════════════════════════════════════════════════════════════════════════
livery_cfg = """[Version]
major = 1
minor = 0

[EDITABLE_COLORS]
editable_color.0 = color: 255,242,204

[PALETTE_LABELS]
label_key.0 = "Type"
label_value.0 = "Traffic"

[SELECTION]
"""
write(SIMOBJ_DIR / "liveries" / "asobo" / "traffic_default" / "livery.cfg", livery_cfg)

# ═════════════════════════════════════════════════════════════════════════════
#  6. PRESETS  — exact structure from working package
# ═════════════════════════════════════════════════════════════════════════════
preset_sim_cfg = f"""[FLTSIM.0]
title = "{SIMOBJ_NAME}"
"""
write(SIMOBJ_DIR / "presets" / AUTHOR / "default" / "config" / "sim.cfg", preset_sim_cfg)

attached_objects_cfg = f"""[SIM_ATTACHMENT.0]
attachment_root="SimObjects\\GroundVehicles\\{SIMOBJ_NAME}\\attachments\\{AUTHOR}\\Part_Exterior"
attachment_file="model/Exterior.xml"
attach_to_model="exterior"
attach_to_model_minsize=0
"""
write(SIMOBJ_DIR / "presets" / AUTHOR / "default" / "config" / "attached_objects.cfg", attached_objects_cfg)

# stub files — exact copy from working package (48 bytes each)
stub = "; generated file\n\n[MODULAR_MERGE]\nauto = true\n"
for stub_name in ["ai.cfg","cameras.cfg","cockpit.cfg","engines.cfg",
                   "flight_model.cfg","gameplay.cfg","reference_points.cfg","systems.cfg"]:
    write(SIMOBJ_DIR / "presets" / AUTHOR / "default" / "config" / stub_name, stub)

# ═════════════════════════════════════════════════════════════════════════════
#  7. ATTACHMENTS  — our emissive dot model
# ═════════════════════════════════════════════════════════════════════════════
attachment_cfg = """[Version]
major = 1
minor = 0

[Tags]
tag.0 = "car_exterior"
"""
write(SIMOBJ_DIR / "attachments" / AUTHOR / "part_exterior" / "attachment.cfg", attachment_cfg)

# exterior.xml for attachment — points to our GLTF model
exterior_xml_attach = """<?xml version="1.0" encoding="utf-8"?>
<ModelInfo>
\t<LODS>
\t\t<LOD minSize="1" ModelFile="LightDot_LOD00.gltf"/>
\t</LODS>
</ModelInfo>
"""
write(SIMOBJ_DIR / "attachments" / AUTHOR / "part_exterior" / "model" / "exterior.xml", exterior_xml_attach)

# No wheel animation — minimal behavior (just empty ModelBehaviors)
behavior_xml = """<ModelBehaviors>
</ModelBehaviors>
"""
write(SIMOBJ_DIR / "attachments" / AUTHOR / "part_exterior" / "model" / "exterior_behavior.xml", behavior_xml)

# ── Build emissive dot GLTF + BIN ────────────────────────────────────────────
# Flat quad: 1.0 m x 1.0 m, Y = 0.30 m above pivot
# Vertex layout: POSITION(VEC3 f32) | NORMAL(VEC3 f32) | TEXCOORD_0(VEC2 f32)
#                12                   12                   8  = 32 bytes / vertex
# 4 vertices × 32 = 128 bytes  +  6 indices × 2 = 12 bytes  = 140 bytes total

HALF  = 0.50   # half-size in metres (1.0 x 1.0 m quad)
Y_OFF = 0.30   # height above ground pivot

# Positions (Y-up, quad in XZ plane)
pos = [
    (-HALF, Y_OFF, -HALF),
    ( HALF, Y_OFF, -HALF),
    ( HALF, Y_OFF,  HALF),
    (-HALF, Y_OFF,  HALF),
]
# Up normal for all vertices
norm = (0.0, 1.0, 0.0)
# UVs
uv = [(0.0,0.0),(1.0,0.0),(1.0,1.0),(0.0,1.0)]

bin_data = bytearray()
for i in range(4):
    p = pos[i]
    bin_data += struct.pack('<3f', p[0], p[1], p[2])   # POSITION
    bin_data += struct.pack('<3f', norm[0], norm[1], norm[2])  # NORMAL
    bin_data += struct.pack('<2f', uv[i][0], uv[i][1])         # TEXCOORD_0
# indices
bin_data += struct.pack('<6H', 0,1,2, 0,2,3)

bin_bytes = bytes(bin_data)
assert len(bin_bytes) == 140, f"unexpected BIN size {len(bin_bytes)}"

gltf_json = {
    "asset": {
        "version": "2.0",
        "generator": "RoadTraffic create_package.py",
        "extensions": {
            "ASOBO_asset_optimized": {
                "MajorVersion": 4,
                "MinorVersion": 6,
                "BoundingBoxMin": [-HALF, Y_OFF, -HALF],
                "BoundingBoxMax": [ HALF, Y_OFF,  HALF]
            },
            "ASOBO_normal_map_convention": {
                "tangent_space_convention": "DirectX"
            }
        }
    },
    "scene": 0,
    "scenes": [{"nodes": [0]}],
    "nodes": [{
        "mesh": 0,
        "name": "LightDot",
        "extensions": {"ASOBO_unique_id": {"id": "LightDot"}}
    }],
    "meshes": [{
        "name": "LightDot",
        "primitives": [{
            "attributes": {
                "POSITION":   0,
                "NORMAL":     1,
                "TEXCOORD_0": 2
            },
            "indices":  3,
            "material": 0,
            "mode":     4
        }]
    }],
    "materials": [{
        "name": "EmissiveDot",
        "pbrMetallicRoughness": {
            "baseColorFactor": [1.0, 0.95, 0.8, 1.0],
            "metallicFactor":  0.0,
            "roughnessFactor": 1.0
        },
        "emissiveFactor": [1.0, 0.95, 0.8],
        "doubleSided": True,
        "alphaMode": "OPAQUE"
    }],
    "accessors": [
        # POSITION
        {"bufferView":0,"byteOffset":0,"componentType":5126,"count":4,"type":"VEC3",
         "min":[-HALF,Y_OFF,-HALF],"max":[HALF,Y_OFF,HALF]},
        # NORMAL
        {"bufferView":0,"byteOffset":48,"componentType":5126,"count":4,"type":"VEC3"},
        # TEXCOORD_0
        {"bufferView":0,"byteOffset":96,"componentType":5126,"count":4,"type":"VEC2"},
        # INDICES
        {"bufferView":1,"byteOffset":0,"componentType":5123,"count":6,"type":"SCALAR"}
    ],
    "bufferViews": [
        {"buffer":0,"byteOffset":0,  "byteLength":128,"target":34962},
        {"buffer":0,"byteOffset":128,"byteLength":12, "target":34963}
    ],
    "buffers": [{"byteLength": 140, "uri": "LightDot_LOD00.bin"}],
    "extensionsUsed": [
        "ASOBO_asset_optimized",
        "ASOBO_normal_map_convention",
        "ASOBO_unique_id"
    ]
}

model_dir = SIMOBJ_DIR / "attachments" / AUTHOR / "part_exterior" / "model"
write(model_dir / "LightDot_LOD00.bin",  bin_bytes)
write(model_dir / "LightDot_LOD00.gltf", json.dumps(gltf_json, indent=2))

# ═════════════════════════════════════════════════════════════════════════════
#  8. LAYOUT.JSON  — all files, lowercase paths, Windows FILETIME
# ═════════════════════════════════════════════════════════════════════════════
content_entries = []
for f in sorted(PKG_DIR.rglob("*")):
    if f.is_file() and f.name != "layout.json":
        rel = f.relative_to(PKG_DIR).as_posix().lower()
        content_entries.append({
            "path": rel,
            "size": f.stat().st_size,
            "date": int((f.stat().st_mtime + WIN_EPOCH) * 10_000_000)
        })

layout = {"content": content_entries}
write(PKG_DIR / "layout.json", json.dumps(layout, indent=2))

# update total_package_size in manifest
total = sum(e["size"] for e in content_entries)
manifest["total_package_size"] = str(total)
write(PKG_DIR / "manifest.json", json.dumps(manifest, indent=2))

# ═════════════════════════════════════════════════════════════════════════════
#  9. ZIP
# ═════════════════════════════════════════════════════════════════════════════
zip_path = SCRIPT_DIR / f"{PKG_NAME}.zip"
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    for f in sorted(PKG_DIR.rglob("*")):
        if f.is_file():
            z.write(f, f.relative_to(SCRIPT_DIR))

print(f"[OK] Package: {PKG_DIR}")
print(f"[OK] ZIP:     {zip_path} ({zip_path.stat().st_size} B)")
print()
print("Files in package:")
for e in content_entries:
    print(f"  {e['size']:6d}  {e['path']}")
print()
print("=== INSTALL ===")
print(f"Copy folder:  {PKG_DIR}")
print(f"Into:  ...\\Microsoft Flight Simulator 2024\\Packages\\Community\\")
print(f"Title to spawn: \"{SIMOBJ_NAME}\"")
