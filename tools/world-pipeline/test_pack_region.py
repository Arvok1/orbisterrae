import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

import numpy as np
import rasterio
from rasterio.transform import from_bounds

from pack_region import MAGIC, SIDE, build


class RegionPackTest(unittest.TestCase):
    def test_pack_is_deterministic_and_preserves_layers(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            arrays = [np.full((8, 8), 1234, dtype=np.int16),
                      np.full((8, 8), 112, dtype=np.uint8),
                      np.full((8, 8), 76, dtype=np.uint8)]
            paths = []
            for name, array in zip(("dem", "cover", "trees"), arrays):
                path = root / f"{name}.tif"
                with rasterio.open(path, "w", driver="GTiff", width=8, height=8,
                                   count=1, dtype=array.dtype, crs="EPSG:4326",
                                   transform=from_bounds(0, 0, .1, .1, 8, 8)) as dst:
                    dst.write(array, 1)
                paths.append(path)
            first = build(*paths, (0, 0, .1, .1), root / "first.orbt")
            second = build(*paths, (0, 0, .1, .1), root / "second.orbt")
            self.assertEqual(first["pack_sha256"], second["pack_sha256"])
            self.assertEqual(first["pack_bytes"], second["pack_bytes"])
            self.assertEqual(json.loads((root / "first.orbt.json").read_text()), first)
            packed = (root / "first.orbt").read_bytes()
            self.assertEqual(packed[:5], MAGIC)
            self.assertEqual(struct.unpack("<I", packed[5:9])[0], SIDE * SIDE * 4)
            payload = zlib.decompress(packed[9:])
            self.assertEqual(np.frombuffer(payload[:SIDE * SIDE * 2], "<i2")[0], 1234)
            self.assertEqual(payload[SIDE * SIDE * 2], 112)
            self.assertEqual(payload[SIDE * SIDE * 3], 76)

    def test_rejects_invalid_bounds(self):
        with self.assertRaisesRegex(ValueError, "invalid WGS84"):
            build("missing", "missing", "missing", (1, 0, 0, 1), "unused")
        with self.assertRaisesRegex(ValueError, "exceeds 100 m"):
            build("missing", "missing", "missing", (0, 0, 1, 1), "unused")
