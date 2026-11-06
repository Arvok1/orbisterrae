using Google.Protobuf;
using NUnit.Framework;
using Orbis.V1;

namespace Orbis.Tests.EditMode
{
    public sealed class ProtocolRoundTripTests
    {
        [Test]
        public void FoundCityEnvelopeRoundTripsWithoutLosingUnicodeOrCoordinates()
        {
            var original = new Envelope
            {
                Seq = 42,
                Command = new Command
                {
                    RequestId = "req-7",
                    FoundCity = new FoundCity
                    {
                        Name = "Vila Aurora ✦",
                        Pos = new LatLon { Lat = -22.9068, Lon = -43.1729 },
                    },
                },
            };

            var decoded = Envelope.Parser.ParseFrom(original.ToByteArray());

            Assert.That(decoded.Seq, Is.EqualTo(42));
            Assert.That(decoded.Command.RequestId, Is.EqualTo("req-7"));
            Assert.That(decoded.Command.FoundCity.Name, Is.EqualTo("Vila Aurora ✦"));
            Assert.That(decoded.Command.FoundCity.Pos.Lat, Is.EqualTo(-22.9068));
            Assert.That(decoded.Command.FoundCity.Pos.Lon, Is.EqualTo(-43.1729));
        }
    }
}
