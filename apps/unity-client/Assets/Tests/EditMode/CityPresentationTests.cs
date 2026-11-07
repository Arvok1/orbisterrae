using System;
using NUnit.Framework;
using Orbis.Core;
using Orbis.V1;

namespace Orbis.Tests.EditMode
{
    public sealed class CityPresentationTests
    {
        [Test]
        public void DecodesExactRuns() => Assert.That(CityPresentation.DecodeRle(new byte[] { 2, 3, 0, 1, 1, 0 }, 4), Is.EqualTo(new byte[] { 2, 2, 2, 1 }));
        [Test]
        public void RejectsInvalidRuns()
        {
            foreach (var bytes in new[] { new byte[] { 1, 0, 0 }, new byte[] { 1, 5, 0 }, new byte[] { 1, 2 } })
                Assert.Throws<ArgumentException>(() => CityPresentation.DecodeRle(bytes, 4));
        }
        [TestCase(0d, 179.999d)]
        [TestCase(80d, -179.999d)]
        public void GridRoundTripsAcrossSeam(double latitude, double longitude)
        {
            var origin = new GeoCoordinate(latitude, longitude);
            var coordinate = CityPresentation.CellCoordinate(origin, 256, 256, 200.5, 40.5);
            Assert.That(CityPresentation.TryCell(origin, 256, 256, coordinate, out var cell), Is.True);
            Assert.That(cell.x, Is.EqualTo(200));
            Assert.That(cell.y, Is.EqualTo(40));
        }
        [Test]
        public void RotationSwapsFootprint()
        {
            var definition = new BuildingDefinition { Width = 2, Height = 3 };
            Assert.That(CityPresentation.Footprint(definition, 1).x, Is.EqualTo(3));
            Assert.That(CityPresentation.Variant("city/building"), Is.EqualTo(CityPresentation.Variant("city/building")));
        }
    }
}
