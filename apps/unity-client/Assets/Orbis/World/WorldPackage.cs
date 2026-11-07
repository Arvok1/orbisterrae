using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace Orbis.World
{
    [Serializable]
    public sealed class WorldPackageManifest
    {
        public string format;
        public int version;
        public int earth_radius_m;
        public int tile_side;
        public int max_lod;
        public long package_bytes;
        public WorldVisualFiles visual;
        public WorldShardDescriptor[] shards;
    }

    [Serializable]
    public sealed class WorldVisualFiles
    {
        public WorldFileDescriptor relief;
        public WorldFileDescriptor biomes;
        public WorldFileDescriptor hydrography;
        public WorldFileDescriptor coastlines;
    }

    [Serializable]
    public sealed class WorldFileDescriptor
    {
        public string path;
        public long bytes;
        public string sha256;
    }

    [Serializable]
    public sealed class WorldShardDescriptor
    {
        public int face;
        public int lod;
        public string path;
        public long bytes;
        public string sha256;
        public int tile_count;
    }

    public readonly struct WorldTileKey : IEquatable<WorldTileKey>
    {
        public readonly int Face;
        public readonly int Lod;
        public readonly int X;
        public readonly int Y;

        public WorldTileKey(int face, int lod, int x, int y)
        {
            Face = face;
            Lod = lod;
            X = x;
            Y = y;
        }

        public bool Equals(WorldTileKey other) => Face == other.Face && Lod == other.Lod && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is WorldTileKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Face, Lod, X, Y);
        public override string ToString() => $"f{Face}/l{Lod}/{X}/{Y}";
    }

    public sealed class WorldHeightTile
    {
        public WorldTileKey Key { get; }
        public int Side { get; }
        public short[] Heights { get; }
        public short MinimumHeight { get; }
        public short MaximumHeight { get; }
        public float GeometricErrorMeters { get; }

        public WorldHeightTile(WorldTileKey key, int side, short[] heights, short minimumHeight,
            short maximumHeight, float geometricErrorMeters)
        {
            Key = key;
            Side = side;
            Heights = heights;
            MinimumHeight = minimumHeight;
            MaximumHeight = maximumHeight;
            GeometricErrorMeters = geometricErrorMeters;
        }

        public float Sample(float tileU, float tileV)
        {
            var x = Mathf.Clamp(tileU, 0f, 1f) * (Side - 1);
            var y = Mathf.Clamp(tileV, 0f, 1f) * (Side - 1);
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var x1 = Mathf.Min(x0 + 1, Side - 1);
            var y1 = Mathf.Min(y0 + 1, Side - 1);
            var a = Mathf.Lerp(Heights[y0 * Side + x0], Heights[y0 * Side + x1], x - x0);
            var b = Mathf.Lerp(Heights[y1 * Side + x0], Heights[y1 * Side + x1], x - x0);
            return Mathf.Lerp(a, b, y - y0);
        }
    }

    public sealed class WorldPackage
    {
        const long MaximumPackageBytes = 8L * 1024 * 1024 * 1024;
        static readonly byte[] Magic = { (byte)'O', (byte)'R', (byte)'B', (byte)'W', (byte)'2', 0, 0, 0 };
        readonly Dictionary<(int Face, int Lod), WorldShardDescriptor> shards = new();
        readonly HashSet<string> verified = new(StringComparer.OrdinalIgnoreCase);

        public string Root { get; }
        public WorldPackageManifest Manifest { get; }

        public WorldPackage(string root)
        {
            Root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
            var manifestPath = Path.Combine(Root, "manifest.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("orbis-world-v2 manifest was not found", manifestPath);
            Manifest = JsonUtility.FromJson<WorldPackageManifest>(File.ReadAllText(manifestPath));
            if (Manifest == null || Manifest.format != "orbis-world-v2" || Manifest.version != 2)
                throw new InvalidDataException("Unsupported world package manifest.");
            if (Manifest.tile_side < 2 || Manifest.tile_side > 257 || Manifest.max_lod < 0 || Manifest.max_lod > 7)
                throw new InvalidDataException("World package dimensions are invalid.");
            if (Manifest.package_bytes < 0 || Manifest.package_bytes > MaximumPackageBytes)
                throw new InvalidDataException("World package exceeds the 8 GB contract.");
            foreach (var descriptor in Manifest.shards ?? Array.Empty<WorldShardDescriptor>())
            {
                if (descriptor.face is < 0 or > 5 || descriptor.lod < 0 || descriptor.lod > Manifest.max_lod)
                    throw new InvalidDataException("World package contains an invalid shard key.");
                shards.Add((descriptor.face, descriptor.lod), descriptor);
            }
        }

        public string ResolveVisual(WorldFileDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.path)) return string.Empty;
            return VerifyFile(descriptor.path, descriptor.bytes, descriptor.sha256);
        }

        public string ResolveContained(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative));
            if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("package_path_escape");
            return path;
        }

        string VerifyFile(string relative, long bytes, string hash)
        {
            var path = ResolveContained(relative);
            lock (verified)
            {
                if (verified.Contains(path)) return path;
                if (bytes < 0 || bytes > MaximumPackageBytes || new FileInfo(path).Length != bytes)
                    throw new InvalidDataException("package_size_mismatch");
                using var stream = File.OpenRead(path);
                using var algorithm = SHA256.Create();
                var actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("package_hash_mismatch");
                verified.Add(path);
                return path;
            }
        }

        public WorldHeightTile ReadTile(WorldTileKey key)
        {
            if (!shards.TryGetValue((key.Face, key.Lod), out var descriptor))
                throw new FileNotFoundException($"Shard for {key} is absent.");
            var axis = 1 << key.Lod;
            if (key.X < 0 || key.X >= axis || key.Y < 0 || key.Y >= axis)
                throw new ArgumentOutOfRangeException(nameof(key));
            var path = VerifyFile(descriptor.path, descriptor.bytes, descriptor.sha256);
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            var magic = reader.ReadBytes(8);
            if (magic.Length != Magic.Length || !BytesEqual(magic, Magic)) throw new InvalidDataException("Invalid world shard magic.");
            var face = reader.ReadByte();
            var lod = reader.ReadByte();
            var side = reader.ReadUInt16();
            var tileCount = reader.ReadUInt32();
            if (face != key.Face || lod != key.Lod || side != Manifest.tile_side || tileCount != axis * axis)
                throw new InvalidDataException("World shard header does not match its manifest.");
            const int entryBytes = 24;
            var tileIndex = key.Y * axis + key.X;
            stream.Seek(16L + tileIndex * entryBytes, SeekOrigin.Begin);
            var offset = reader.ReadUInt64();
            var packedLength = reader.ReadUInt32();
            var rawLength = reader.ReadUInt32();
            var minimum = reader.ReadInt16();
            var maximum = reader.ReadInt16();
            var error = reader.ReadSingle();
            var expectedLength = checked(side * side * sizeof(short));
            if (rawLength != expectedLength || packedLength == 0 || packedLength > expectedLength + 1024
                || offset < 16UL + tileCount * entryBytes || offset > (ulong)stream.Length
                || packedLength > (ulong)stream.Length - offset)
                throw new InvalidDataException("World tile index is corrupt.");
            stream.Seek((long)offset, SeekOrigin.Begin);
            var packed = reader.ReadBytes((int)packedLength);
            using var packedStream = new MemoryStream(packed, false);
            using var deflate = new DeflateStream(packedStream, CompressionMode.Decompress);
            var raw = new byte[rawLength];
            ReadExactly(deflate, raw);
            if (deflate.ReadByte() != -1) throw new InvalidDataException("Oversized world tile.");
            var heights = new short[side * side];
            Buffer.BlockCopy(raw, 0, heights, 0, raw.Length);
            return new WorldHeightTile(key, side, heights, minimum, maximum, error);
        }

        static void ReadExactly(Stream stream, byte[] target)
        {
            var offset = 0;
            while (offset < target.Length)
            {
                var read = stream.Read(target, offset, target.Length - offset);
                if (read == 0) throw new EndOfStreamException("Truncated world tile.");
                offset += read;
            }
        }

        static bool BytesEqual(byte[] left, byte[] right)
        {
            for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }
    }

    public static class CubeSphereProjection
    {
        public static WorldTileKey TileFor(Vector3 normal, int lod, out Vector2 tileUv)
        {
            normal.Normalize();
            var ax = Mathf.Abs(normal.x);
            var ay = Mathf.Abs(normal.y);
            var az = Mathf.Abs(normal.z);
            int face;
            float u;
            float v;
            if (ax >= ay && ax >= az)
            {
                if (normal.x >= 0) { face = 0; u = -normal.z / ax; v = normal.y / ax; }
                else { face = 1; u = normal.z / ax; v = normal.y / ax; }
            }
            else if (ay >= ax && ay >= az)
            {
                if (normal.y >= 0) { face = 2; u = normal.x / ay; v = -normal.z / ay; }
                else { face = 3; u = normal.x / ay; v = normal.z / ay; }
            }
            else
            {
                if (normal.z >= 0) { face = 4; u = normal.x / az; v = normal.y / az; }
                else { face = 5; u = -normal.x / az; v = normal.y / az; }
            }
            var axis = 1 << lod;
            var faceU = Mathf.Clamp01((u + 1f) * 0.5f);
            var faceV = Mathf.Clamp01((v + 1f) * 0.5f);
            var scaledX = Mathf.Min(faceU * axis, axis - Mathf.Epsilon);
            var scaledY = Mathf.Min(faceV * axis, axis - Mathf.Epsilon);
            var x = Mathf.FloorToInt(scaledX);
            var y = Mathf.FloorToInt(scaledY);
            tileUv = new Vector2(scaledX - x, scaledY - y);
            return new WorldTileKey(face, lod, x, y);
        }

        public static Vector3 Direction(WorldTileKey key, float tileU, float tileV)
        {
            var axis = 1 << key.Lod;
            var u = -1f + 2f * (key.X + tileU) / axis;
            var v = -1f + 2f * (key.Y + tileV) / axis;
            var direction = key.Face switch
            {
                0 => new Vector3(1f, v, -u),
                1 => new Vector3(-1f, v, u),
                2 => new Vector3(u, 1f, -v),
                3 => new Vector3(u, -1f, v),
                4 => new Vector3(u, v, 1f),
                5 => new Vector3(-u, v, -1f),
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };
            return direction.normalized;
        }
    }
}
