"""Export real hydrography, RESOLVE biomes and continuous ETOPO relief."""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import urllib.request
import zipfile

import numpy as np
import shapefile
from shapely.geometry import Point, shape
from shapely.prepared import prep
from PIL import Image, ImageDraw
from world import load_land, SOURCE_SHA256

SOURCES = {
    "rivers": "https://naturalearth.s3.amazonaws.com/10m_physical/ne_10m_rivers_lake_centerlines.zip",
    "lakes": "https://naturalearth.s3.amazonaws.com/50m_physical/ne_50m_lakes.zip",
    "biomes": "https://storage.googleapis.com/teow2016/Ecoregions2017.zip",
}
SOURCE_HASHES = {
    "rivers": "ded71b01870855ccfe19b51f2ec14c9bb48fae23c0e9f3c11974d426433b5c38",
    "lakes": "f28d42c286d96b57a17aac2cbeb432f8c65532c20063495711fbc64e24666df3",
    "biomes": "be36d6209e443038d02e309f0447c6e7f2a62f5fe60c605ffe90d064952f2a60",
}
BIOME_COLORS = {
    1: '#89ae65', 2: '#b0ba70', 3: '#86a767', 4: '#a7b96b', 5: '#91ab72',
    6: '#92afa0', 7: '#d3cc7e', 8: '#b6c681', 9: '#98bc97', 10: '#b8b08c',
    11: '#c9cfbb', 12: '#c2bf7b', 13: '#e7ce8d', 14: '#77a98a',
}
RASTER_SIZE = 4096


def raster_point(coordinate):
    lon, lat = coordinate
    lat = max(-85.05112878, min(85.05112878, lat))
    sine = math.sin(math.radians(lat))
    return ((lon + 180) / 360 * RASTER_SIZE,
            (0.5 - math.log((1 + sine) / (1 - sine)) / (4 * math.pi)) * RASTER_SIZE)


def paint_region(image, polygons, biome):
    mask = Image.new('L', image.size)
    draw = ImageDraw.Draw(mask)
    for polygon in polygons:
        for index, ring in enumerate(polygon):
            draw.polygon([raster_point(point) for point in ring], fill=255 if index == 0 else 0)
    image.paste(BIOME_COLORS.get(biome, '#e5e5d4'), (0, 0), mask)


def download(kind, cache):
    path = cache / f"{kind}.zip"
    if not path.exists():
        temporary = path.with_suffix(".download")
        print(f"Downloading {kind}...", flush=True)
        with urllib.request.urlopen(SOURCES[kind], timeout=120) as response:
            with temporary.open("wb") as stream:
                while chunk := response.read(1024 * 1024):
                    stream.write(chunk)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None:
                raise ValueError(f"Corrupt {kind} archive")
        temporary.replace(path)
    return path


def records(path):
    with zipfile.ZipFile(path) as archive:
        names = [name[:-4] for name in archive.namelist()
                 if name.endswith(".shp") and "__MACOSX" not in name]
        if len(names) != 1:
            raise ValueError(f"Expected one shapefile in {path}: {names}")
        base = names[0]
        reader = shapefile.Reader(
            shp=io.BytesIO(archive.read(base + ".shp")),
            shx=io.BytesIO(archive.read(base + ".shx")),
            dbf=io.BytesIO(archive.read(base + ".dbf")),
            encodingErrors="replace",
        )
        for record in reader.iterShapeRecords():
            yield shape(record.shape.__geo_interface__), record.record.as_dict()


def line_parts(geometry):
    if geometry.geom_type == "LineString":
        return [[[round(x, 4), round(y, 4)] for x, y in geometry.coords]]
    if geometry.geom_type in ("MultiLineString", "GeometryCollection"):
        return [part for g in geometry.geoms for part in line_parts(g)]
    return []


def polygon_parts(geometry):
    if geometry.geom_type == "Polygon":
        return [[[[round(x, 4), round(y, 4)] for x, y in ring.coords]
                 for ring in [geometry.exterior, *geometry.interiors]]]
    if geometry.geom_type in ("MultiPolygon", "GeometryCollection"):
        return [part for g in geometry.geoms for part in polygon_parts(g)]
    return []


def relief_points(elevation, land):
    points = []
    # Each 2-degree region gets its highest 0.5-degree land sample.
    # These are relief symbols, not a claimed catalogue of named summits.
    for row in range(0, 360, 4):
        for column in range(0, 720, 4):
            block = elevation[row:row + 4, column:column + 4]
            offset = np.unravel_index(np.argmax(block), block.shape)
            i, j = row + offset[0], column + offset[1]
            height = float(elevation[i, j])
            lat, lon = 90 - (i + 0.5) / 2, -180 + (j + 0.5) / 2
            if height >= 700 and land.covers(Point(lon, lat)):
                points.append([round(lon, 2), round(lat, 2), round(height)])
    return points


def relief_raster(elevation, size=RASTER_SIZE):
    """Deterministic Mercator hillshade and hypsometry; ocean remains transparent."""
    rows, columns = elevation.shape
    gradient_y, gradient_x = np.gradient(elevation.astype(np.float64))
    slope = np.arctan(np.hypot(gradient_x, gradient_y) * 0.018)
    aspect = np.arctan2(-gradient_x, gradient_y)
    azimuth, altitude = math.radians(315), math.radians(38)
    shade = np.sin(altitude) * np.cos(slope) + np.cos(altitude) * np.sin(slope) * np.cos(azimuth - aspect)
    shade = np.clip((shade + 0.35) / 1.35, 0, 1)
    pixel_y = np.arange(size, dtype=np.float64)
    mercator_lat = np.degrees(np.arctan(np.sinh(math.pi * (1 - 2 * (pixel_y + 0.5) / size))))
    source_y = np.clip(((90 - mercator_lat) / 180 * rows).astype(int), 0, rows - 1)
    source_x = np.clip(((np.arange(size) + 0.5) / size * columns).astype(int), 0, columns - 1)
    sampled_elevation = elevation[source_y[:, None], source_x[None, :]].astype(np.float64)
    sampled_shade = shade[source_y[:, None], source_x[None, :]]
    height = np.clip(sampled_elevation, 0, 5000) / 5000
    low = np.array([111, 118, 73], dtype=np.float64)
    high = np.array([184, 169, 135], dtype=np.float64)
    snow = np.array([231, 226, 211], dtype=np.float64)
    color = low + (high - low) * np.minimum(height[..., None] * 2.4, 1)
    snow_mix = np.clip((sampled_elevation - 2800) / 1800, 0, 1)[..., None]
    color = color * (1 - snow_mix) + snow * snow_mix
    dramatic = 0.42 + sampled_shade[..., None] * 0.82
    color = np.clip(color * dramatic, 0, 255).astype(np.uint8)
    alpha = np.where(sampled_elevation >= 0, 190, 0).astype(np.uint8)
    return Image.fromarray(np.dstack((color, alpha)), mode='RGBA')


def feature_class(attributes):
    for key, value in attributes.items():
        if str(key).lower() in ('featurecla', 'feature_class'):
            return str(value)
    return ''


def river_style(attributes):
    rank = int(attributes.get('scalerank') or attributes.get('SCALERANK') or 10)
    return rank, round(max(0.8, 4.6 - rank * 0.34), 2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--download-only", action="store_true")
    parser.add_argument("--output-dir", type=Path, default=Path("apps/client/src/assets"))
    args = parser.parse_args()
    root = Path("data/world/v0")
    cache = root / "terrain-sources"
    cache.mkdir(parents=True, exist_ok=True)
    paths = {kind: download(kind, cache) for kind in SOURCES}
    for kind, path in paths.items():
        with path.open('rb') as stream:
            if hashlib.file_digest(stream, 'sha256').hexdigest() != SOURCE_HASHES[kind]:
                raise ValueError(f'Source hash changed: {kind}')
    if args.download_only:
        for kind, path in paths.items():
            geometry, attributes = next(records(path))
            print(kind, geometry.geom_type, list(attributes)[:15], flush=True)
        return
    content = (root / "ne_110m_land.zip").read_bytes()
    if hashlib.sha256(content).hexdigest() != SOURCE_SHA256:
        raise ValueError("Land source does not match P0.3")
    land = prep(load_land(content))
    elevation = np.load(root / "elevation_05deg.npy")
    output = {"version": 2, "rivers": [], "lakes": [], "biomes": [], "sources": {}}
    lake_centerlines_filtered = 0
    for geometry, attrs in records(paths["rivers"]):
        if feature_class(attrs).strip().lower() == 'lake centerline':
            lake_centerlines_filtered += 1
            continue
        lines = line_parts(geometry.simplify(0.025, preserve_topology=True))
        if lines:
            rank, width = river_style(attrs)
            output["rivers"].append({"name": attrs.get("name") or "", "rank": rank,
                                      "width": width, "lines": lines})
    print(f"Exported {len(output['rivers'])} rivers", flush=True)
    for geometry, attrs in records(paths["lakes"]):
        polygons = polygon_parts(geometry.simplify(0.025, preserve_topology=True))
        if polygons:
            output["lakes"].append({"name": attrs.get("name") or "", "polygons": polygons})
    biome_image = Image.new('RGBA', (RASTER_SIZE, RASTER_SIZE))
    for number, (geometry, attrs) in enumerate(records(paths["biomes"]), 1):
        biome = int(attrs["BIOME_NUM"])
        simplified = geometry.simplify(0.08, preserve_topology=True)
        polygons = polygon_parts(simplified)
        if polygons:
            paint_region(biome_image, polygons, biome)
            output["biomes"].append({"biome": biome, "name": attrs.get("ECO_NAME") or ""})
        if number % 100 == 0:
            print(f'Processed {number} ecoregions', flush=True)
    for kind, path in paths.items():
        with path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        output["sources"][kind] = {"url": SOURCES[kind], "sha256": digest}
    output["stats"] = {kind: len(output[kind]) for kind in ("rivers", "lakes", "biomes")}
    output["stats"]["lake_centerlines_filtered"] = lake_centerlines_filtered
    args.output_dir.mkdir(parents=True, exist_ok=True)
    target = args.output_dir / "world-terrain-v1.json"
    raster_target = target.with_name('world-biomes-v1.png')
    relief_target = target.with_name('world-relief-v2.png')
    raster_temporary = raster_target.with_suffix('.tmp')
    biome_image.save(raster_temporary, format='PNG')
    raster_temporary.replace(raster_target)
    relief_temporary = relief_target.with_suffix('.tmp')
    relief_raster(elevation).save(relief_temporary, format='PNG')
    relief_temporary.replace(relief_target)
    temporary = target.with_suffix(".tmp")
    temporary.write_text(json.dumps(output, separators=(",", ":")) + "\n", encoding="utf-8")
    temporary.replace(target)
    print(json.dumps(output["stats"]), flush=True)


if __name__ == "__main__":
    main()
