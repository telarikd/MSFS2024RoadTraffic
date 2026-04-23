#!/usr/bin/env python3
"""
create_glb.py
=============
Generates RoadTrafficLight.glb — a minimal HDR-emissive quad for MSFS 2024.

Design:
  - Flat 0.40 x 0.40 m quad (XZ plane, horizontal on terrain)
  - Pivot at Y=0 (ground contact), visual mesh at Y=0.30 m (avoids Z-fighting)
  - Material: emissiveFactor [1.0, 0.95, 0.80] × emissiveStrength 30
    → triggers MSFS HDR bloom → visible from 15–20 km altitude
  - Double-sided (visible from all angles)
  - KHR_materials_emissive_strength extension (glTF 2.0 standard, MSFS 2024 supported)

No external dependencies — pure Python stdlib (struct + json).

Output:
  RoadTrafficLight/SimObjects/GroundVehicles/RoadTrafficLight/model/RoadTrafficLight.glb
"""

import os
import json
import struct

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_PATH   = os.path.join(
    SCRIPT_DIR,
    "RoadTrafficLight", "SimObjects", "GroundVehicles",
    "RoadTrafficLight", "model", "RoadTrafficLight.glb"
)

# ══════════════════════════════════════════════════════════════════════════════
#  GEOMETRY CONSTANTS
# ══════════════════════════════════════════════════════════════════════════════
HALF  = 0.50   # half-size → quad is 1.00 m × 1.00 m
Y_OFF = 2.00   # metres above ground → well above uneven terrain surface

# ══════════════════════════════════════════════════════════════════════════════
#  BINARY HELPERS
# ══════════════════════════════════════════════════════════════════════════════
def f32(*v): return struct.pack(f'<{len(v)}f', *v)
def u16(*v): return struct.pack(f'<{len(v)}H', *v)


def build_buffer():
    """Pack all vertex/index data into one contiguous binary buffer."""
    S, Y = HALF, Y_OFF

    # 4 vertices, positions (x, y, z)  — CCW winding viewed from above
    pos = (f32(-S, Y, -S) +   # 0 front-left
           f32( S, Y, -S) +   # 1 front-right
           f32( S, Y,  S) +   # 2 back-right
           f32(-S, Y,  S))    # 3 back-left
    # 4 × 3 × 4 = 48 bytes

    # Normals — all pointing up (0, 1, 0)
    nrm = f32(0, 1, 0) * 4
    # 4 × 3 × 4 = 48 bytes

    # UV coords (required by the mesh primitive, not used by emissive-only material)
    uvs = (f32(0.0, 1.0) +
           f32(1.0, 1.0) +
           f32(1.0, 0.0) +
           f32(0.0, 0.0))
    # 4 × 2 × 4 = 32 bytes

    # Indices: two CCW triangles → 6 × uint16 = 12 bytes (already 4-byte aligned)
    idx = u16(0, 1, 2,  0, 2, 3)
    # 12 bytes

    # Byte offsets inside the single buffer
    off_pos = 0
    off_nrm = off_pos + len(pos)   # 48
    off_uvs = off_nrm + len(nrm)   # 96
    off_idx = off_uvs + len(uvs)   # 128

    buf = pos + nrm + uvs + idx    # 140 bytes, divisible by 4 ✓
    assert len(buf) % 4 == 0, "Buffer must be 4-byte aligned"

    return buf, off_pos, off_nrm, off_uvs, off_idx, len(pos)+len(nrm)+len(uvs)


def build_gltf(buf_len, off_pos, off_nrm, off_uvs, off_idx, vtx_block_len):
    """Return the glTF JSON descriptor as a Python dict."""
    S, Y = HALF, Y_OFF
    return {
        "asset": {
            "version":   "2.0",
            "generator": "RoadTraffic create_glb.py"
        },
        "scene":  0,
        "scenes": [{"nodes": [0]}],
        "nodes":  [{"mesh": 0, "name": "LightQuad"}],

        # ── Mesh ──────────────────────────────────────────────────────────
        "meshes": [{
            "name": "LightQuad",
            "primitives": [{
                "attributes": {
                    "POSITION":   0,
                    "NORMAL":     1,
                    "TEXCOORD_0": 2
                },
                "indices":  3,
                "material": 0,
                "mode": 4          # GL_TRIANGLES
            }]
        }],

        # ── Material ──────────────────────────────────────────────────────
        # Warm-white emissive (no base texture needed).
        # emissiveStrength 30 → HDR value that triggers MSFS bloom engine.
        # Visible as glowing dot from 15–20 km altitude at night.
        "materials": [{
            "name": "LightEmissive",
            "pbrMetallicRoughness": {
                "baseColorFactor": [1.0, 0.95, 0.80, 1.0],
                "metallicFactor":  0.0,
                "roughnessFactor": 1.0
            },
            "emissiveFactor": [1.0, 0.95, 0.80],
            "doubleSided":    True,
            "alphaMode":      "OPAQUE",
            "extensions": {
                "KHR_materials_emissive_strength": {
                    "emissiveStrength": 30.0
                }
            }
        }],

        # ── Accessors ─────────────────────────────────────────────────────
        "accessors": [
            # 0: POSITION (VEC3 float32)
            {
                "bufferView": 0, "byteOffset": off_pos,
                "componentType": 5126, "count": 4, "type": "VEC3",
                "max": [ S, Y,  S],
                "min": [-S, Y, -S]
            },
            # 1: NORMAL (VEC3 float32)
            {
                "bufferView": 0, "byteOffset": off_nrm,
                "componentType": 5126, "count": 4, "type": "VEC3"
            },
            # 2: TEXCOORD_0 (VEC2 float32)
            {
                "bufferView": 0, "byteOffset": off_uvs,
                "componentType": 5126, "count": 4, "type": "VEC2"
            },
            # 3: INDICES (SCALAR uint16)
            {
                "bufferView": 1, "byteOffset": 0,
                "componentType": 5123, "count": 6, "type": "SCALAR"
            }
        ],

        # ── Buffer views ──────────────────────────────────────────────────
        "bufferViews": [
            # BV 0: vertex attributes (ARRAY_BUFFER)
            {
                "buffer":     0,
                "byteOffset": 0,
                "byteLength": vtx_block_len,
                "target":     34962
            },
            # BV 1: index data (ELEMENT_ARRAY_BUFFER)
            {
                "buffer":     0,
                "byteOffset": off_idx,
                "byteLength": 12,      # 6 × uint16
                "target":     34963
            }
        ],

        "buffers": [{"byteLength": buf_len}],

        "extensionsUsed":     ["KHR_materials_emissive_strength"],
        "extensionsRequired": []
    }


def assemble_glb(gltf_dict, binary_buf):
    """Pack glTF JSON + binary into a GLB container (spec §3.6)."""
    # JSON chunk — padded with spaces to 4-byte alignment
    json_bytes = json.dumps(gltf_dict, separators=(',', ':')).encode('utf-8')
    while len(json_bytes) % 4:
        json_bytes += b' '

    total = 12 + 8 + len(json_bytes) + 8 + len(binary_buf)

    glb  = struct.pack('<III', 0x46546C67, 2, total)        # header: magic, ver, len
    glb += struct.pack('<II',  len(json_bytes), 0x4E4F534A)  # JSON chunk header
    glb += json_bytes
    glb += struct.pack('<II',  len(binary_buf), 0x004E4942)  # BIN\0 chunk header
    glb += binary_buf
    return glb


def main():
    buf, off_pos, off_nrm, off_uvs, off_idx, vtx_len = build_buffer()
    gltf   = build_gltf(len(buf), off_pos, off_nrm, off_uvs, off_idx, vtx_len)
    glb    = assemble_glb(gltf, buf)

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    with open(OUT_PATH, 'wb') as fh:
        fh.write(glb)

    print(f"[OK] GLB created: {OUT_PATH}")
    print(f"     Size: {len(glb)} bytes  |  Buffer: {len(buf)} bytes")
    print(f"     Quad: {HALF*2:.2f} m × {HALF*2:.2f} m  @  Y={Y_OFF:.2f} m above pivot")
    print(f"     Emissive: warm-white × 30 (HDR bloom in MSFS)")


if __name__ == '__main__':
    main()
