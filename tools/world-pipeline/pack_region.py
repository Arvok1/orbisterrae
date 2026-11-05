"""Build a reproducible, compact regional terrain pack from local Copernicus rasters.

Inputs are deliberately explicit: the global source archives are never bundled with
the game or silently downloaded by this command.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import zlib

import numpy as np
import rasterio
from rasterio.enums import Resampling
from rasterio.warp import reproject
from rasterio.transform import from_bounds


SIDE = 256
MAGIC = b"ORBT1"


def digest(path):
    result = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def band(path, bounds, index, resampling, dtype):
    output = np.zeros((SIDE, SIDE), dtype=dtype)
    with rasterio.open(path) as source:
        if source.crs.to_epsg() != 4326:
            raise ValueError(f"{path}: expected EPSG:4326 source")
        if (source.bounds.left > bounds[0] or source.bounds.bottom > bounds[1]
                or source.bounds.right < bounds[2] or source.bounds.top < bounds[3]):
            raise ValueError(f"{path}: source does not cover requested bounds")
        reproject(
            source=rasterio.band(source, index), destination=output,
            src_transform=source.transform, src_crs=source.crs,
            src_nodata=source.nodata,
            dst_transform=from_bounds(*bounds, SIDE, SIDE), dst_crs="EPSG:4326",
            dst_nodata=0, resampling=resampling,
        )
    return output


def build(elevation, cover, trees, bounds, output):
    if not (-180 <= bounds[0] < bounds[2] <= 180 and
            -90 <= bounds[1] < bounds[3] <= 90):
        raise ValueError("invalid WGS84 bounds")
    latitude = (bounds[1] + bounds[3]) / 2
    metres_per_sample = max(
        (bounds[3] - bounds[1]) * math.pi / 180 * 6_371_000 / SIDE,
        (bounds[2] - bounds[0]) * math.pi / 180 * 6_371_000
        * math.cos(math.radians(latitude)) / SIDE,
    )
    if metres_per_sample > 100:
        raise ValueError("region exceeds 100 m output sampling; use smaller bounds")
    height = band(elevation, bounds, 1, Resampling.bilinear, np.float32)
    if not np.isfinite(height).all():
        raise ValueError("elevation contains non-finite values")
    height = np.clip(np.rint(height), -32768, 32767).astype("<i2")
    classes = band(cover, bounds, 1, Resampling.nearest, np.uint8)
    tree_fraction = band(trees, bounds, 1, Resampling.bilinear, np.uint8)
    payload = height.tobytes() + classes.tobytes() + tree_fraction.tobytes()
    packed = MAGIC + struct.pack("<I", len(payload)) + zlib.compress(payload, 9)
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(packed)
    metadata = {
        "format": "orbis-terrain-v1", "bounds_wgs84": list(bounds),
        "width": SIDE, "height": SIDE, "elevation_unit": "metre",
        "source_elevation_resolution_m": 90, "source_cover_resolution_m": 100,
        "output_sample_spacing_m_max": round(metres_per_sample, 2),
        "layers": ["elevation_i16_le", "land_class_u8", "tree_fraction_u8"],
        "sources": {
            "elevation": {"dataset": "Copernicus DEM GLO-90", "sha256": digest(elevation)},
            "cover": {"dataset": "Copernicus LC100 2019 class", "sha256": digest(cover)},
            "trees": {"dataset": "Copernicus LC100 2019 tree fraction", "sha256": digest(trees)},
        },
        "pack_sha256": hashlib.sha256(packed).hexdigest(),
        "pack_bytes": len(packed),
    }
    output.with_suffix(output.suffix + ".json").write_text(
        json.dumps(metadata, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return metadata


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--elevation", required=True)
    parser.add_argument("--cover", required=True)
    parser.add_argument("--trees", required=True)
    parser.add_argument("--bounds", nargs=4, type=float, required=True,
                        metavar=("WEST", "SOUTH", "EAST", "NORTH"))
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.elevation, args.cover, args.trees,
                           args.bounds, args.output)))
