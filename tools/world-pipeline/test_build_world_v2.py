import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

import numpy as np
import rasterio
from rasterio.transform import from_bounds

from build_world_v2 import ENTRY, HEADER, MAGIC, TILE_SIDE, build, cube_direction


class WorldV2Test(unittest.TestCase):
    def test_cube_faces_are_normalized(self):
        sample = np.array([[0.25]])
        for face in range(6):
            x, y, z = cube_direction(face, sample, -sample)
            self.assertAlmostEqual(float((x * x + y * y + z * z).item()), 1.0, places=6)

    def test_build_is_deterministic_and_decodable(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            elevation = root / "elevation.tif"
            values = np.arange(64 * 128, dtype=np.int16).reshape((64, 128)) - 1000
            with rasterio.open(elevation, "w", driver="GTiff", width=128, height=64,
                               count=1, dtype=values.dtype, crs="EPSG:4326",
                               transform=from_bounds(-180, -90, 180, 90, 128, 64)) as target:
                target.write(values, 1)
            assets = []
            for name in ("relief.png", "biomes.png", "hydro.json"):
                path = root / name
                path.write_bytes(name.encode("ascii"))
                assets.append(path)
            first = build(elevation, *assets, root / "first", 1, 8 * 1024 * 1024)
            second = build(elevation, *assets, root / "second", 1, 8 * 1024 * 1024)
            self.assertEqual([item["sha256"] for item in first["shards"]],
                             [item["sha256"] for item in second["shards"]])
            manifest = json.loads((root / "first" / "manifest.json").read_text())
            self.assertEqual(manifest["format"], "orbis-world-v2")
            shard = root / "first" / manifest["shards"][0]["path"]
            with shard.open("rb") as stream:
                magic, face, lod, side, count = HEADER.unpack(stream.read(HEADER.size))
                self.assertEqual((magic, face, lod, side, count), (MAGIC, 0, 0, TILE_SIDE, 1))
                offset, packed, raw, _, _, _ = ENTRY.unpack(stream.read(ENTRY.size))
                stream.seek(offset)
                payload = zlib.decompress(stream.read(packed), wbits=-15)
                self.assertEqual(len(payload), raw)
                self.assertEqual(len(payload), TILE_SIDE * TILE_SIDE * 2)

    def test_size_limit_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            elevation = root / "elevation.tif"
            values = np.zeros((8, 16), dtype=np.int16)
            with rasterio.open(elevation, "w", driver="GTiff", width=16, height=8,
                               count=1, dtype=values.dtype, crs="EPSG:4326",
                               transform=from_bounds(-180, -90, 180, 90, 16, 8)) as target:
                target.write(values, 1)
            asset = root / "asset"
            asset.write_bytes(b"asset")
            with self.assertRaisesRegex(ValueError, "above limit"):
                build(elevation, asset, asset, asset, root / "world", 0, 1)


if __name__ == "__main__":
    unittest.main()
