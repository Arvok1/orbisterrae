"""Build the deterministic offline Orbis world package from local source data.

The package stores cube-sphere height tiles in a small number of seekable shards.
It never downloads data; acquisition and licensing remain explicit operations.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import struct
import tempfile
import zlib

import numpy as np
import rasterio


MAGIC = b"ORBW2\0\0\0"
TILE_SIDE = 65
ENTRY = struct.Struct("<QIIhhf")
HEADER = struct.Struct("<8sBBHI")
EARTH_RADIUS_M = 6_371_000


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def cube_direction(face: int, u: np.ndarray, v: np.ndarray):
    one = np.ones_like(u)
    if face == 0:
        x, y, z = one, v, -u
    elif face == 1:
        x, y, z = -one, v, u
    elif face == 2:
        x, y, z = u, one, -v
    elif face == 3:
        x, y, z = u, -one, v
    elif face == 4:
        x, y, z = u, v, one
    elif face == 5:
        x, y, z = -u, v, -one
    else:
        raise ValueError("cube face must be between zero and five")
    length = np.sqrt(x * x + y * y + z * z)
    return x / length, y / length, z / length


def sample_tile(elevation: np.ndarray, transform, face: int, lod: int, tile_x: int, tile_y: int):
    count = 1 << lod
    local = np.linspace(0.0, 1.0, TILE_SIDE, dtype=np.float64)
    u = -1.0 + 2.0 * (tile_x + local) / count
    v = -1.0 + 2.0 * (tile_y + local) / count
    grid_u, grid_v = np.meshgrid(u, v)
    x, y, z = cube_direction(face, grid_u, grid_v)
    longitude = np.degrees(np.arctan2(x, z))
    latitude = np.degrees(np.arcsin(np.clip(y, -1.0, 1.0)))
    columns = np.rint((longitude - transform.c) / transform.a).astype(np.int64)
    rows = np.rint((latitude - transform.f) / transform.e).astype(np.int64)
    columns %= elevation.shape[1]
    rows = np.clip(rows, 0, elevation.shape[0] - 1)
    values = elevation[rows, columns]
    values = np.nan_to_num(values, nan=0.0, posinf=32767.0, neginf=-32768.0)
    return np.clip(np.rint(values), -32768, 32767).astype("<i2")


def raw_deflate(payload: bytes) -> bytes:
    compressor = zlib.compressobj(level=7, wbits=-15)
    return compressor.compress(payload) + compressor.flush()


def write_shard(root: Path, elevation: np.ndarray, transform, face: int, lod: int):
    tile_count_axis = 1 << lod
    tile_count = tile_count_axis * tile_count_axis
    path = root / "shards" / f"face-{face}-lod-{lod}.orbw"
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    header_bytes = HEADER.size + tile_count * ENTRY.size
    entries = []
    with temporary.open("wb") as stream:
        stream.write(b"\0" * header_bytes)
        for tile_y in range(tile_count_axis):
            for tile_x in range(tile_count_axis):
                heights = sample_tile(elevation, transform, face, lod, tile_x, tile_y)
                raw = heights.tobytes(order="C")
                packed = raw_deflate(raw)
                offset = stream.tell()
                stream.write(packed)
                geometric_error = math.pi * EARTH_RADIUS_M / (tile_count_axis * (TILE_SIDE - 1))
                entries.append((offset, len(packed), len(raw), int(heights.min()), int(heights.max()), geometric_error))
        stream.seek(0)
        stream.write(HEADER.pack(MAGIC, face, lod, TILE_SIDE, tile_count))
        for entry in entries:
            stream.write(ENTRY.pack(*entry))
    temporary.replace(path)
    return {
        "face": face,
        "lod": lod,
        "path": path.relative_to(root).as_posix(),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
        "tile_count": tile_count,
    }


def copy_asset(source: Path, destination: Path):
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    return {"path": destination.name, "bytes": destination.stat().st_size, "sha256": sha256(destination)}


def build(elevation_path: Path, relief_path: Path, biome_path: Path, hydrography_path: Path,
          output: Path, max_lod: int, max_bytes: int):
    if not 0 <= max_lod <= 7:
        raise ValueError("max LOD must be between zero and seven")
    for source in (elevation_path, relief_path, biome_path, hydrography_path):
        if not source.is_file():
            raise FileNotFoundError(source)

    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="orbis-world-v2-", dir=output.parent) as temp_dir:
        staging = Path(temp_dir) / "world-v2"
        staging.mkdir()
        with rasterio.open(elevation_path) as source:
            # NOAA distributes ETOPO 2022 with compound EPSG:9518
            # (WGS84 horizontal + EGM2008 vertical). Its horizontal grid is
            # still global longitude/latitude and is valid for this sampler.
            if source.crs is None or source.crs.to_epsg() not in (4326, 9518):
                raise ValueError("elevation source must use WGS84 longitude/latitude (EPSG:4326 or 9518)")
            elevation = source.read(1)
            transform = source.transform
            shards = []
            for lod in range(max_lod + 1):
                for face in range(6):
                    shards.append(write_shard(staging, elevation, transform, face, lod))

        visual = {
            "relief": copy_asset(relief_path, staging / "global-relief.png"),
            "biomes": copy_asset(biome_path, staging / "global-biomes.png"),
            "hydrography": copy_asset(hydrography_path, staging / "hydrography.json"),
        }
        coastline = hydrography_path.with_name("world-land-v1.json")
        if coastline.is_file():
            visual["coastlines"] = copy_asset(coastline, staging / "coastlines.json")
        total = sum(path.stat().st_size for path in staging.rglob("*") if path.is_file())
        if total > max_bytes:
            raise ValueError(f"world package is {total} bytes, above limit {max_bytes}")
        manifest = {
            "format": "orbis-world-v2",
            "version": 2,
            "seed": 12022017,
            "earth_radius_m": EARTH_RADIUS_M,
            "tile_side": TILE_SIDE,
            "max_lod": max_lod,
            "compression": "deflate-raw",
            "height_encoding": "int16-little-endian-metres",
            "projection": "cube-sphere-wgs84",
            "package_bytes": total,
            "sources": {
                "elevation": {"dataset": "ETOPO 2022", "resolution_arc_seconds": 60, "sha256": sha256(elevation_path)},
                "biomes": {"dataset": "RESOLVE Ecoregions 2017", "sha256": sha256(biome_path), "projection": "web-mercator"},
                "hydrography": {"dataset": "Natural Earth", "sha256": sha256(hydrography_path)},
            },
            "visual": visual,
            "shards": shards,
        }
        manifest_path = staging / "manifest.json"
        manifest_path.write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        if output.exists():
            backup = output.with_name(output.name + ".previous")
            if backup.exists():
                shutil.rmtree(backup)
            output.replace(backup)
        staging.replace(output)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elevation", type=Path, required=True)
    parser.add_argument("--relief", type=Path, required=True)
    parser.add_argument("--biomes", type=Path, required=True)
    parser.add_argument("--hydrography", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--max-lod", type=int, default=5)
    parser.add_argument("--max-bytes", type=int, default=8 * 1024 * 1024 * 1024)
    arguments = parser.parse_args()
    print(json.dumps(build(arguments.elevation, arguments.relief, arguments.biomes,
                           arguments.hydrography, arguments.output, arguments.max_lod,
                           arguments.max_bytes), sort_keys=True))


if __name__ == "__main__":
    main()
