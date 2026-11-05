using NUnit.Framework;
using Orbis.Core;

namespace Orbis.Tests.EditMode
{
    public sealed class GeoMathTests
    {
        [TestCase(0, 0, 0)]
        [TestCase(-22.9068, -43.1729, 15)]
        [TestCase(89.5, 179.9, 4200)]
        public void EcefRoundTripPreservesGeodeticCoordinates(double latitude, double longitude, double altitude)
        {
            var original = new GeoCoordinate(latitude, longitude, altitude);
            var roundTrip = GeoMath.FromEcef(GeoMath.ToEcef(original));

            Assert.That(roundTrip.LatitudeDegrees, Is.EqualTo(latitude).Within(1e-8));
            Assert.That(roundTrip.LongitudeDegrees, Is.EqualTo(longitude).Within(1e-8));
            Assert.That(roundTrip.AltitudeMeters, Is.EqualTo(altitude).Within(1e-5));
        }

        [Test]
        public void UnityScaleUsesOneUnitPerKilometer()
        {
            var origin = new Double3(0, 0, 0);
            var point = new Double3(1000, -2500, 42_000);

            var unity = point.ToUnityKilometers(origin);

            Assert.That(unity.x, Is.EqualTo(1));
            Assert.That(unity.y, Is.EqualTo(-2.5f));
            Assert.That(unity.z, Is.EqualTo(42));
        }
    }
}
