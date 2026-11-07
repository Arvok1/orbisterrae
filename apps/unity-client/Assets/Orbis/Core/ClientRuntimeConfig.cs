using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Orbis.Core
{
    public enum MapVisualStyle
    {
        Relief,
        Manuscript,
    }

    public sealed class ClientRuntimeConfig
    {
        public Uri HttpEndpoint { get; private set; }
        public Uri WebSocketEndpoint { get; private set; }
        public string TerrainPackageRoot { get; private set; }
        public string Locale { get; private set; }
        public int QualityLevel { get; private set; }
        public int CacheLimitMegabytes { get; private set; }
        public string TestUsername { get; private set; }
        public string TestPassword { get; private set; }
        public bool HasTestCredentials => !string.IsNullOrWhiteSpace(TestUsername) && !string.IsNullOrEmpty(TestPassword);

        ClientRuntimeConfig(
            Uri httpEndpoint,
            Uri webSocketEndpoint,
            string terrainPackageRoot,
            string locale,
            int qualityLevel,
            int cacheLimitMegabytes,
            string testUsername,
            string testPassword)
        {
            HttpEndpoint = httpEndpoint;
            WebSocketEndpoint = webSocketEndpoint;
            TerrainPackageRoot = terrainPackageRoot;
            Locale = locale;
            QualityLevel = qualityLevel;
            CacheLimitMegabytes = cacheLimitMegabytes;
            TestUsername = testUsername;
            TestPassword = testPassword;
        }

        public static ClientRuntimeConfig CreateDefaults(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException("A persistent data path is required.", nameof(persistentDataPath));
            }

            return new ClientRuntimeConfig(
                new Uri("http://127.0.0.1:8080", UriKind.Absolute),
                new Uri("ws://127.0.0.1:8080/ws", UriKind.Absolute),
                Path.Combine(persistentDataPath, "world-packages"),
                "pt-BR",
                2,
                4096,
                null,
                null);
        }

        public static ClientRuntimeConfig Parse(ClientRuntimeConfig defaults, IEnumerable<string> arguments)
        {
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            var result = new ClientRuntimeConfig(
                defaults.HttpEndpoint,
                defaults.WebSocketEndpoint,
                defaults.TerrainPackageRoot,
                defaults.Locale,
                defaults.QualityLevel,
                defaults.CacheLimitMegabytes,
                defaults.TestUsername,
                defaults.TestPassword);

            foreach (var argument in arguments ?? Array.Empty<string>())
            {
                if (TryRead(argument, "--http", out var http))
                {
                    result.HttpEndpoint = ParseEndpoint(http, new[] { "http", "https" }, "HTTP");
                }
                else if (TryRead(argument, "--ws", out var webSocket))
                {
                    result.WebSocketEndpoint = ParseEndpoint(webSocket, new[] { "ws", "wss" }, "WebSocket");
                }
                else if (TryRead(argument, "--terrain-root", out var terrainRoot))
                {
                    result.TerrainPackageRoot = Path.GetFullPath(terrainRoot);
                }
                else if (TryRead(argument, "--locale", out var locale))
                {
                    result.Locale = NormalizeLocale(locale);
                }
                else if (TryRead(argument, "--quality", out var quality))
                {
                    result.QualityLevel = ParseNonNegativeInt(quality, "quality");
                }
                else if (TryRead(argument, "--cache-mb", out var cache))
                {
                    result.CacheLimitMegabytes = Math.Max(256, ParseNonNegativeInt(cache, "cache-mb"));
                }
                else if (TryRead(argument, "--user", out var user))
                {
                    result.TestUsername = user;
                }
                else if (TryRead(argument, "--password", out var password))
                {
                    result.TestPassword = password;
                }
            }

            return result;
        }

        static bool TryRead(string argument, string key, out string value)
        {
            var prefix = key + "=";
            if (!string.IsNullOrEmpty(argument) && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Substring(prefix.Length).Trim();
                return value.Length > 0;
            }

            value = string.Empty;
            return false;
        }

        static Uri ParseEndpoint(string value, IReadOnlyCollection<string> schemes, string label)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || !schemes.Contains(endpoint.Scheme))
            {
                throw new ArgumentException($"Invalid {label} endpoint: {value}");
            }

            return endpoint;
        }

        static int ParseNonNegativeInt(string value, string label)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            {
                throw new ArgumentException($"Invalid {label} value: {value}");
            }

            return parsed;
        }

        static string NormalizeLocale(string locale)
        {
            if (locale.Equals("en", StringComparison.OrdinalIgnoreCase) || locale.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (locale.Equals("pt", StringComparison.OrdinalIgnoreCase) || locale.Equals("pt-BR", StringComparison.OrdinalIgnoreCase))
            {
                return "pt-BR";
            }

            throw new ArgumentException($"Unsupported locale: {locale}");
        }
    }
}
