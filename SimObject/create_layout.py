#!/usr/bin/env python3
"""
create_layout.py
================
Generates layout.json for the RoadTrafficLight MSFS package.

MSFS requires layout.json to list every file in the package with:
  - relative path (forward slashes)
  - size in bytes
  - date as Windows FILETIME (100-ns intervals since 1601-01-01)

Run AFTER create_glb.py so the GLB is already in place.
"""

import os
import json

SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
PACKAGE_DIR = os.path.join(SCRIPT_DIR, "RoadTrafficLight")
LAYOUT_PATH = os.path.join(PACKAGE_DIR, "layout.json")

# Seconds between Windows epoch (1601-01-01) and Unix epoch (1970-01-01)
_WIN_EPOCH_OFFSET = 11_644_473_600


def unix_to_filetime(unix_ts):
    """Convert Unix timestamp (float) to Windows FILETIME (int)."""
    return int((unix_ts + _WIN_EPOCH_OFFSET) * 10_000_000)


def generate(package_dir):
    if not os.path.isdir(package_dir):
        print(f"[ERROR] Package directory not found: {package_dir}")
        return

    entries = []
    for root, dirs, files in os.walk(package_dir):
        # Sort for deterministic output
        dirs.sort()
        for fname in sorted(files):
            if fname == "layout.json":
                continue  # never include layout.json itself
            abs_path = os.path.join(root, fname)
            rel_path = os.path.relpath(abs_path, package_dir).replace("\\", "/")
            entries.append({
                "path": rel_path,
                "size": os.path.getsize(abs_path),
                "date": unix_to_filetime(os.path.getmtime(abs_path))
            })

    layout = {
        "version": 1,
        "paths":   entries
    }

    with open(LAYOUT_PATH, 'w', encoding='utf-8') as fh:
        json.dump(layout, fh, indent=2)

    print(f"[OK] layout.json created: {LAYOUT_PATH}")
    print(f"     Listed {len(entries)} file(s):")
    for e in entries:
        print(f"       {e['path']}  ({e['size']} B)")


if __name__ == '__main__':
    generate(PACKAGE_DIR)
