using System.Collections.Generic;
using NUnit.Framework;
using Orbis.World;
using UnityEngine;

namespace Orbis.Tests.EditMode
{
    public sealed class WorldPackageTests
    {
        [Test]
        public void CubeSphereRoundTripSelectsEveryFaceAndTileCentre()
        {
            for (var face = 0; face < 6; face++)
            {
                var key = new WorldTileKey(face, 3, 4, 2);
                var direction = CubeSphereProjection.Direction(key, 0.5f, 0.5f);
                var selected = CubeSphereProjection.TileFor(direction, key.Lod, out var tileUv);
                Assert.That(selected, Is.EqualTo(key));
                Assert.That(tileUv.x, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(tileUv.y, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
            }
        }

        [Test]
        public void HeightTileSamplesBilinearly()
        {
            var tile = new WorldHeightTile(new WorldTileKey(0, 0, 0, 0), 2,
                new short[] { 0, 100, 200, 300 }, 0, 300, 1f);
            Assert.That(tile.Sample(0.5f, 0.5f), Is.EqualTo(150f).Within(0.001f));
            Assert.That(tile.Sample(-1f, 2f), Is.EqualTo(200f));
        }

        [Test]
        public void MosaicRingCoversTheCentreAcrossCubeFaceSeams()
        {
            for (var face = 0; face < 6; face++)
            for (var lod = 0; lod <= 5; lod++)
            {
                var axis = 1 << lod;
                var corner = new WorldTileKey(face, lod, axis - 1, 0);
                var ring = CubeSphereProjection.MosaicRing(corner);
                Assert.That(ring.Contains(corner), Is.True, $"{corner} must anchor its own ring");
                Assert.That(ring.Count, Is.AtLeast(3));
                Assert.That(ring.Count, Is.AtMost(9));
                Assert.That(new HashSet<WorldTileKey>(ring).Count, Is.EqualTo(ring.Count), "ring must be de-duplicated");
                foreach (var key in ring) Assert.That(key.Lod, Is.EqualTo(corner.Lod));
                var direction = CubeSphereProjection.Direction(corner, 0.5f, 0.5f);
                Assert.That(CubeSphereProjection.TileFor(direction, corner.Lod, out _), Is.EqualTo(corner));
            }
        }

        [Test]
        public void MosaicRingIsDeterministicForTheSameSeed()
        {
            // Uma única string é comparável em qualquer harness (e outside the editor via cs-check).
            Assert.That(string.Join(";", SnapshotRandomRings(12022017)), Is.EqualTo(string.Join(";", SnapshotRandomRings(12022017))));
        }

        static List<string> SnapshotRandomRings(int seed)
        {
            var random = new System.Random(seed);
            var result = new List<string>();
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var lod = random.Next(6);
                var axis = 1 << lod;
                var center = new WorldTileKey(random.Next(6), lod, random.Next(axis), random.Next(axis));
                result.Add(string.Join("|", CubeSphereProjection.MosaicRing(center).ConvertAll(key => key.ToString())));
            }
            return result;
        }
    }
}
