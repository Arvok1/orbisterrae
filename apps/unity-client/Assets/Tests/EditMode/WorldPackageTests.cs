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
    }
}
