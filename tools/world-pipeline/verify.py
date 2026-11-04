"""Verify generated artifacts before the client or server consumes them."""
import hashlib
import json
from pathlib import Path

import numpy as np
import pyarrow.parquet as pq
from PIL import Image


def verify(root):
    manifest = json.loads((root / "manifest.json").read_text())
    assert manifest["max_zoom"] == 6, "Expected complete z0-z6 tile pyramid"
    for zoom in range(7):
        for x in range(2 ** zoom):
            for y in range(2 ** zoom):
                path = root / "tiles" / str(zoom) / str(x) / f"{y}.png"
                with Image.open(path) as image:
                    assert image.size == (256, 256), f"Invalid tile: {path}"
                    image.verify()
    table = pq.read_table(root / "biome_h3.parquet")
    assert len(table) == manifest["cell_count"]
    assert len(set(table["h3"].to_pylist())) == len(table)
    assert table["elevation_m"].null_count == 0
    assert np.isfinite(table["elevation_m"].to_numpy()).all()
    assert min(table["elevation_m"].to_pylist()) < -5000
    assert max(table["elevation_m"].to_pylist()) > 4000
    for name, key in [("ne_110m_land.zip", "source_sha256"),
                      ("etopo_2022_60s.tif", "elevation_sha256")]:
        with (root / name).open("rb") as stream:
            assert hashlib.file_digest(stream, "sha256").hexdigest() == manifest[key]
    print(f"Verified 5461 tiles and {len(table)} cells with finite elevation", flush=True)


if __name__ == "__main__":
    verify(Path("data/world/v0"))
