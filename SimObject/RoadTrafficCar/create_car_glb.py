#!/usr/bin/env python3
"""Generate a dependency-free low-poly GLB traffic car for MSFS GroundVehicle."""

import json
import os
import struct


ROOT = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(
    ROOT, "SimObjects", "GroundVehicles", "RoadTrafficCar", "model", "RoadTrafficCar.glb")
LAYOUT_PATH = os.path.join(ROOT, "layout.json")


MATERIALS = [
    ("BodyPaint", [0.72, 0.035, 0.025, 1.0], 0.15, 0.45),
    ("Glass", [0.035, 0.10, 0.16, 1.0], 0.0, 0.18),
    ("Tyre", [0.025, 0.025, 0.025, 1.0], 0.0, 0.92),
    ("Wheel", [0.28, 0.30, 0.33, 1.0], 0.72, 0.28),
    ("Headlamp", [1.0, 0.91, 0.62, 1.0], 0.0, 0.28),
    ("TailLamp", [0.80, 0.015, 0.01, 1.0], 0.0, 0.30),
]


def pad4(data, fill=b"\x00"):
    return data + fill * ((-len(data)) % 4)


def append_box(group, center, size):
    """Add a box with flat normals to one material group."""
    cx, cy, cz = center
    sx, sy, sz = (value * 0.5 for value in size)
    corners = [
        (cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
        (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz),
        (cx - sx, cy - sy, cz + sz), (cx + sx, cy - sy, cz + sz),
        (cx + sx, cy + sy, cz + sz), (cx - sx, cy + sy, cz + sz),
    ]
    faces = [
        ((0, 1, 2, 3), (0, 0, -1)), ((5, 4, 7, 6), (0, 0, 1)),
        ((4, 0, 3, 7), (-1, 0, 0)), ((1, 5, 6, 2), (1, 0, 0)),
        ((3, 2, 6, 7), (0, 1, 0)), ((4, 5, 1, 0), (0, -1, 0)),
    ]
    for face, normal in faces:
        start = len(group["positions"])
        for index in face:
            group["positions"].append(corners[index])
            group["normals"].append(normal)
        group["indices"].extend((start, start + 1, start + 2, start, start + 2, start + 3))


def append_wheel(group, center, radius=0.34, half_width=0.15, sides=10):
    """Add a low-poly wheel cylinder whose axle runs along X."""
    cx, cy, cz = center
    start = len(group["positions"])
    for side in range(sides):
        angle = side * 2.0 * 3.141592653589793 / sides
        y = cy + radius * __import__("math").sin(angle)
        z = cz + radius * __import__("math").cos(angle)
        normal = (0, __import__("math").sin(angle), __import__("math").cos(angle))
        group["positions"].append((cx - half_width, y, z))
        group["normals"].append(normal)
        group["positions"].append((cx + half_width, y, z))
        group["normals"].append(normal)
    for side in range(sides):
        next_side = (side + 1) % sides
        left = start + side * 2
        right = left + 1
        next_left = start + next_side * 2
        next_right = next_left + 1
        group["indices"].extend((left, next_left, right, right, next_left, next_right))


def append_wheel_hub(group, center, radius=0.19, half_width=0.155, sides=10):
    append_wheel(group, center, radius, half_width, sides)


def build_geometry():
    groups = [{"positions": [], "normals": [], "indices": []} for _ in MATERIALS]
    body, glass, tyre, wheel, headlamp, taillamp = groups

    # Coordinates: X = width, Y = up, Z = forward. Pivot is at ground level.
    append_box(body, (0.0, 0.62, 0.0), (1.78, 0.72, 4.15))
    append_box(body, (0.0, 1.10, -1.72), (1.80, 0.32, 0.54))
    append_box(body, (0.0, 1.13, 1.72), (1.80, 0.26, 0.45))
    append_box(glass, (0.0, 1.30, -0.20), (1.52, 0.72, 1.85))
    append_box(glass, (0.0, 1.30, 0.83), (1.54, 0.67, 0.10))
    append_box(glass, (0.0, 1.30, -1.16), (1.54, 0.67, 0.10))

    for x in (-0.91, 0.91):
        for z in (-1.36, 1.36):
            append_wheel(tyre, (x, 0.36, z))
            append_wheel_hub(wheel, (x, 0.36, z))

    for x in (-0.55, 0.55):
        append_box(headlamp, (x, 0.67, 2.09), (0.38, 0.20, 0.05))
        append_box(taillamp, (x, 0.67, -2.09), (0.34, 0.18, 0.05))

    return groups


def pack_floats(values):
    return struct.pack("<%sf" % len(values), *values)


def create_glb():
    groups = build_geometry()
    binary = bytearray()
    buffer_views = []
    accessors = []
    primitives = []

    def add_view(payload, target):
        offset = len(binary)
        binary.extend(pad4(payload))
        buffer_views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(payload), "target": target})
        return len(buffer_views) - 1

    for material_index, group in enumerate(groups):
        if not group["indices"]:
            continue

        positions = [component for vertex in group["positions"] for component in vertex]
        normals = [component for normal in group["normals"] for component in normal]
        position_view = add_view(pack_floats(positions), 34962)
        normal_view = add_view(pack_floats(normals), 34962)
        index_view = add_view(struct.pack("<%sH" % len(group["indices"]), *group["indices"]), 34963)

        min_pos = [min(vertex[axis] for vertex in group["positions"]) for axis in range(3)]
        max_pos = [max(vertex[axis] for vertex in group["positions"]) for axis in range(3)]
        position_accessor = len(accessors)
        accessors.append({"bufferView": position_view, "componentType": 5126, "count": len(group["positions"]), "type": "VEC3", "min": min_pos, "max": max_pos})
        normal_accessor = len(accessors)
        accessors.append({"bufferView": normal_view, "componentType": 5126, "count": len(group["normals"]), "type": "VEC3"})
        index_accessor = len(accessors)
        accessors.append({"bufferView": index_view, "componentType": 5123, "count": len(group["indices"]), "type": "SCALAR"})
        primitives.append({
            "attributes": {"POSITION": position_accessor, "NORMAL": normal_accessor},
            "indices": index_accessor,
            "material": material_index,
            "mode": 4,
        })

    materials = []
    for name, colour, metallic, roughness in MATERIALS:
        materials.append({
            "name": name,
            "pbrMetallicRoughness": {
                "baseColorFactor": colour,
                "metallicFactor": metallic,
                "roughnessFactor": roughness,
            },
        })
    materials[4]["emissiveFactor"] = [1.0, 0.91, 0.62]
    materials[4]["extensions"] = {"KHR_materials_emissive_strength": {"emissiveStrength": 3.0}}
    materials[5]["emissiveFactor"] = [0.80, 0.015, 0.01]
    materials[5]["extensions"] = {"KHR_materials_emissive_strength": {"emissiveStrength": 2.0}}

    document = {
        "asset": {"version": "2.0", "generator": "RoadTraffic low-poly car generator"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": "RoadTrafficCar", "mesh": 0}],
        "meshes": [{"name": "RoadTrafficCar", "primitives": primitives}],
        "materials": materials,
        "accessors": accessors,
        "bufferViews": buffer_views,
        "buffers": [{"byteLength": len(binary)}],
        "extensionsUsed": ["KHR_materials_emissive_strength"],
    }

    json_bytes = pad4(json.dumps(document, separators=(",", ":")).encode("utf-8"), b" ")
    glb = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(json_bytes) + 8 + len(binary))
    glb += struct.pack("<II", len(json_bytes), 0x4E4F534A) + json_bytes
    glb += struct.pack("<II", len(binary), 0x004E4942) + binary
    return glb


def write_layout():
    entries = []
    for directory, _, files in os.walk(ROOT):
        for filename in files:
            absolute = os.path.join(directory, filename)
            if absolute == LAYOUT_PATH or filename.endswith(".py") or filename.endswith(".bat") or filename.endswith(".md"):
                continue
            relative = os.path.relpath(absolute, ROOT).replace(os.sep, "\\")
            entries.append({"path": relative, "size": os.path.getsize(absolute)})
    with open(LAYOUT_PATH, "w", encoding="utf-8") as stream:
        json.dump({"content": sorted(entries, key=lambda entry: entry["path"])}, stream, indent=2)
        stream.write("\n")


def main():
    os.makedirs(os.path.dirname(MODEL_PATH), exist_ok=True)
    with open(MODEL_PATH, "wb") as stream:
        stream.write(create_glb())
    write_layout()
    print("Created %s" % MODEL_PATH)
    print("Created %s" % LAYOUT_PATH)


if __name__ == "__main__":
    main()
