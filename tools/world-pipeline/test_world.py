import unittest
from shapely.geometry import MultiPolygon, Polygon
import numpy as np
from world import project, build_cells, elevation_at, TILE_SIZE


class GeographyTest(unittest.TestCase):
    def test_elevation_coordinates_and_edges(self):
        grid = np.array([[10, 20], [30, 40]])
        self.assertEqual(elevation_at(grid, 90, -180), 10)
        self.assertEqual(elevation_at(grid, -90, 180), 40)
        self.assertEqual(elevation_at(grid, 45, 90), 20)

    def test_mercator_origin_and_poles(self):
        self.assertEqual(project(0, 0, TILE_SIZE), (128, 128))
        self.assertAlmostEqual(project(0, 90, TILE_SIZE)[1], 0)
        self.assertAlmostEqual(project(0, -90, TILE_SIZE)[1], TILE_SIZE)

    def test_cell_grid_covers_globe_and_has_unique_ids(self):
        land = MultiPolygon([Polygon([(-1, -1), (1, -1), (1, 1), (-1, 1)])])
        table = build_cells(land, 4)
        self.assertEqual(len(table), 288122)
        self.assertEqual(len(set(table["h3"].to_pylist())), len(table))
        self.assertIn(True, table["is_land"].to_pylist())
        self.assertIn(False, table["is_land"].to_pylist())
        self.assertEqual(table.to_pylist(), build_cells(land, 4).to_pylist())


if __name__ == "__main__":
    unittest.main()
