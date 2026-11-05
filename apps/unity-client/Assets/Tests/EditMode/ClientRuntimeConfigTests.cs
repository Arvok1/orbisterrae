using System;
using NUnit.Framework;
using Orbis.Core;

namespace Orbis.Tests.EditMode
{
    public sealed class ClientRuntimeConfigTests
    {
        [Test]
        public void CommandLineOverridesDefaults()
        {
            var defaults = ClientRuntimeConfig.CreateDefaults("C:/orbis-test");
            var parsed = ClientRuntimeConfig.Parse(defaults, new[]
            {
                "--http=https://gateway.example",
                "--ws=wss://gateway.example/ws",
                "--locale=en",
                "--quality=3",
                "--cache-mb=1024",
            });

            Assert.That(parsed.HttpEndpoint, Is.EqualTo(new Uri("https://gateway.example")));
            Assert.That(parsed.WebSocketEndpoint, Is.EqualTo(new Uri("wss://gateway.example/ws")));
            Assert.That(parsed.Locale, Is.EqualTo("en"));
            Assert.That(parsed.QualityLevel, Is.EqualTo(3));
            Assert.That(parsed.CacheLimitMegabytes, Is.EqualTo(1024));
        }

        [Test]
        public void CacheCannotBeConfiguredBelowSafeFloor()
        {
            var defaults = ClientRuntimeConfig.CreateDefaults("C:/orbis-test");
            var parsed = ClientRuntimeConfig.Parse(defaults, new[] { "--cache-mb=8" });

            Assert.That(parsed.CacheLimitMegabytes, Is.EqualTo(256));
        }

        [Test]
        public void InvalidWebSocketSchemeIsRejected()
        {
            var defaults = ClientRuntimeConfig.CreateDefaults("C:/orbis-test");
            Assert.Throws<ArgumentException>(() => ClientRuntimeConfig.Parse(defaults, new[] { "--ws=http://gateway/ws" }));
        }
    }
}
