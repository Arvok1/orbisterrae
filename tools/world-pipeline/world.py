"""Reproducible low-resolution coastlines, Web Mercator tiles and H3 land mask."""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import urllib.request
import zipfile

import h3
import pyarrow as pa
import pyarrow.parquet as pq
from PIL import Image, ImageDraw
import shapefile
from shapely.geometry import Point, shape
from shapely.ops import unary_union
from shapely.prepared import prep

SOURCE = "https://naturalearth.s3.amazonaws.com/110m_physical/ne_110m_land.zip"
SOURCE_SHA256 = "1926c621afd6ac67c3f36639bb1236134a48d82226dc675d3e3df53d02d2a3de"
TILE_SIZE = 256
MERCATOR_LIMIT = 85.0511287798066


def project(lon, lat, size):
    latitude = math.radians(max(-MERCATOR_LIMIT, min(MERCATOR_LIMIT, lat)))
    return ((lon + 180) / 360 * size,
            (1 - math.asinh(math.tan(latitude)) / math.pi) / 2 * size)


def load_land(archive):
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        reader = shapefile.Reader(
            shp=io.BytesIO(bundle.read("ne_110m_land.shp")),
            shx=io.BytesIO(bundle.read("ne_110m_land.shx")),
            dbf=io.BytesIO(bundle.read("ne_110m_land.dbf")),
        )
        return unary_union([shape(item.__geo_interface__) for item in reader.shapes()])


def render_tiles(land, destination, max_zoom):
    # Render once at maximum resolution; lower zooms are deterministic downsampling.
    size = TILE_SIZE * 2 ** max_zoom
    image = Image.new("RGB", (size, size), "#163b48")
    draw = ImageDraw.Draw(image)
    for polygon in land.geoms:
        draw.polygon([project(lon, lat, size) for lon, lat in polygon.exterior.coords],
                     fill="#92a47b")
        for interior in polygon.interiors:
            draw.polygon([project(lon, lat, size) for lon, lat in interior.coords],
                         fill="#163b48")
    for zoom in range(max_zoom + 1):
        count = 2 ** zoom
        level = image.resize((TILE_SIZE * count, TILE_SIZE * count),
                             Image.Resampling.LANCZOS)
        for x in range(count):
            folder = destination / "tiles" / str(zoom) / str(x)
            folder.mkdir(parents=True, exist_ok=True)
            for y in range(count):
                level.crop((x * TILE_SIZE, y * TILE_SIZE,
                            (x + 1) * TILE_SIZE, (y + 1) * TILE_SIZE)).save(folder / f"{y}.png")


def build_cells(land, resolution):
    prepared = prep(land)
    rows = []
    for base in sorted(h3.get_res0_cells()):
        for cell in sorted(h3.cell_to_children(base, resolution)):
            lat, lon = h3.cell_to_latlng(cell)
            is_land = prepared.covers(Point(lon, lat))
            rows.append({"h3": cell, "lat": lat, "lon": lon, "is_land": is_land,
                         "terrain": "land" if is_land else "water"})
    return pa.Table.from_pylist(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path("data/world/v0"))
    parser.add_argument("--max-zoom", type=int, default=6, choices=range(7))
    parser.add_argument("--resolution", type=int, default=4, choices=(4, 5))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    cached = args.output / "ne_110m_land.zip"
    if not cached.exists():
        with urllib.request.urlopen(SOURCE, timeout=60) as response:
            content = response.read()
        # Validate before caching an incomplete download.
        with zipfile.ZipFile(io.BytesIO(content)) as bundle:
            if bundle.testzip() is not None:
                raise ValueError("Corrupt Natural Earth archive")
        cached.write_bytes(content)
    content = cached.read_bytes()
    if hashlib.sha256(content).hexdigest() != SOURCE_SHA256:
        raise ValueError("Natural Earth checksum changed; review the source before updating the pin")
    land = load_land(content)
    print("Rendering coastlines...", flush=True)
    render_tiles(land, args.output, args.max_zoom)
    print("Classifying H3 cells...", flush=True)
    table = build_cells(land, args.resolution)
    pq.write_table(table, args.output / "biome_h3.parquet", compression="zstd")
    (args.output / "land.geojson").write_text(
        json.dumps(land.__geo_interface__, separators=(",", ":")), encoding="utf-8")
    manifest = {
        "version": "v0", "source": SOURCE,
        "source_sha256": hashlib.sha256(content).hexdigest(),
        "h3_resolution": args.resolution, "max_zoom": args.max_zoom,
        "projection": "EPSG:3857", "tile_size": TILE_SIZE,
        "cell_count": len(table),
        "limitations": ["land/water only; elevation, climate and fertility are not yet generated"],
    }
    (args.output / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(table)} cells in {args.output}", flush=True)


if __name__ == "__main__":
    main()
