import unittest
import numpy as np
from shapely.geometry import Polygon, MultiLineString
from PIL import Image
from terrain import line_parts, polygon_parts, relief_points, paint_region, raster_point


class TerrainTest(unittest.TestCase):
    def test_biome_raster_preserves_holes_and_mercator_center(self):
        self.assertEqual(raster_point((0, 0)), (2048, 2048))
        image = Image.new('RGBA', (4096, 4096))
        paint_region(image, [[[[0, 0], [10, 0], [10, 10], [0, 10]],
                              [[4, 4], [6, 4], [6, 6], [4, 6]]]], 1)
        solid = tuple(round(value) for value in raster_point((2, 2)))
        hole = tuple(round(value) for value in raster_point((5, 5)))
        self.assertEqual(image.getpixel(solid)[3], 255)
        self.assertEqual(image.getpixel(hole)[3], 0)

    def test_export_preserves_lines_and_polygon_holes(self):
        lines = MultiLineString([[(1, 2), (2, 3)], [(4, 5), (5, 6)]])
        self.assertEqual(len(line_parts(lines)), 2)
        area = Polygon([(0, 0), (4, 0), (4, 4), (0, 4)],
                       holes=[[(1, 1), (2, 1), (2, 2), (1, 2)]])
        self.assertEqual(len(polygon_parts(area)[0]), 2)

    def test_relief_is_deterministic_and_excludes_water(self):
        grid = np.zeros((360, 720))
        grid[100, 100] = 3000
        grid[200, 200] = 2000
        land = Polygon([(-140, 30), (-120, 30), (-120, 50), (-140, 50)])
        first = relief_points(grid, land)
        self.assertEqual(first, relief_points(grid, land))
        self.assertEqual(len(first), 1)
        self.assertEqual(first[0][2], 3000)


if __name__ == "__main__":
    unittest.main()
