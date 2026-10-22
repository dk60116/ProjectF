using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    // CPU mask cache only: no render meshes, heights or simulation dependencies.
    // Terrain chunks begin half a cell before their integer origin.
    internal sealed class RailBallastMask
    {
        internal const int PixelsPerUnit = 16;
        private const float ShoulderFeather = .3f;
        internal readonly int TileSize, PixelSize;
        internal readonly Dictionary<Vector2Int, Tile> Tiles = new Dictionary<Vector2Int, Tile>();
        internal readonly List<Tile> Changed = new List<Tile>();
        private readonly Dictionary<object, Source> sources = new Dictionary<object, Source>();
        private readonly HashSet<Tile> dirty = new HashSet<Tile>();

        internal sealed class Tile
        {
            internal Vector2Int Key;
            internal byte[] Pixels;
            internal readonly HashSet<Source> Sources = new HashSet<Source>();
        }
        internal sealed class Source
        {
            internal readonly List<Segment> Segments = new List<Segment>();
            internal readonly HashSet<Tile> Tiles = new HashSet<Tile>();
            internal float Radius;
        }
        internal readonly struct Segment
        {
            internal readonly Vector2 A, B;
            internal Segment(Vector2 a, Vector2 b) { A = a; B = b; }
        }

        internal RailBallastMask(int tileSize)
        {
            TileSize = Mathf.Max(4, tileSize);
            PixelSize = TileSize * PixelsPerUnit + 2; // one-pixel bilinear border
        }

        internal void Upsert(object key, IReadOnlyList<Vector2> path, float radius)
        {
            Remove(key);
            if (key == null || path == null || path.Count < 2) return;
            var source = new Source { Radius = Mathf.Max(.05f, radius) };
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i];
                if (!Finite(a) || !Finite(b) || (b - a).sqrMagnitude < .00000001f) continue;
                // Limit raster bounds on long diagonal segments to a narrow band.
                int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 4f));
                for (int j = 0; j < pieces; j++)
                    source.Segments.Add(new Segment(Vector2.Lerp(a, b, j / (float)pieces), Vector2.Lerp(a, b, (j + 1f) / pieces)));
            }
            if (source.Segments.Count == 0) return;
            sources.Add(key, source);
            float extent = source.Radius + 1f / PixelsPerUnit;
            for (int i = 0; i < source.Segments.Count; i++)
            {
                Segment segment = source.Segments[i];
                Vector2Int min = Key(Vector2.Min(segment.A, segment.B) - Vector2.one * extent);
                Vector2Int max = Key(Vector2.Max(segment.A, segment.B) + Vector2.one * extent);
                for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
                {
                    var tileKey = new Vector2Int(x, z);
                    if (!Tiles.TryGetValue(tileKey, out Tile tile))
                        Tiles.Add(tileKey, tile = new Tile { Key = tileKey, Pixels = new byte[PixelSize * PixelSize] });
                    if (!source.Tiles.Add(tile)) continue;
                    tile.Sources.Add(source);
                    dirty.Add(tile);
                }
            }
        }

        internal void Remove(object key)
        {
            if (key == null || !sources.TryGetValue(key, out Source source)) return;
            sources.Remove(key);
            foreach (Tile tile in source.Tiles) { tile.Sources.Remove(source); dirty.Add(tile); }
        }

        internal bool Rebuild()
        {
            Changed.Clear();
            if (dirty.Count == 0) return false;
            foreach (Tile tile in dirty)
            {
                Array.Clear(tile.Pixels, 0, tile.Pixels.Length);
                foreach (Source source in tile.Sources)
                    for (int i = 0; i < source.Segments.Count; i++) Raster(tile, source.Segments[i], source.Radius);
                if (tile.Sources.Count == 0) Tiles.Remove(tile.Key);
                Changed.Add(tile);
            }
            dirty.Clear();
            return true;
        }

        private void Raster(Tile tile, Segment segment, float radius)
        {
            Vector2 firstPixel = Origin(tile.Key) - Vector2.one * (.5f / PixelsPerUnit);
            float extent = radius + .5f / PixelsPerUnit;
            Vector2 low = (Vector2.Min(segment.A, segment.B) - Vector2.one * extent - firstPixel) * PixelsPerUnit;
            Vector2 high = (Vector2.Max(segment.A, segment.B) + Vector2.one * extent - firstPixel) * PixelsPerUnit;
            int minX = Mathf.Max(0, Mathf.CeilToInt(low.x)), minZ = Mathf.Max(0, Mathf.CeilToInt(low.y));
            int maxX = Mathf.Min(PixelSize - 1, Mathf.FloorToInt(high.x)), maxZ = Mathf.Min(PixelSize - 1, Mathf.FloorToInt(high.y));
            Vector2 delta = segment.B - segment.A;
            float inverseLength = 1f / delta.sqrMagnitude;
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                Vector2 point = firstPixel + new Vector2(x, z) / PixelsPerUnit;
                float t = Mathf.Clamp01(Vector2.Dot(point - segment.A, delta) * inverseLength);
                float distance = Vector2.Distance(point, segment.A + delta * t);
                byte coverage = (byte)Mathf.RoundToInt(Mathf.Clamp01((radius - distance) / ShoulderFeather) * 255f);
                int pixel = z * PixelSize + x;
                if (coverage > tile.Pixels[pixel]) tile.Pixels[pixel] = coverage;
            }
        }

        internal Vector2Int Key(Vector2 point) => new Vector2Int(Mathf.FloorToInt((point.x + .5f) / TileSize), Mathf.FloorToInt((point.y + .5f) / TileSize));
        internal Vector2 Origin(Vector2Int key) => new Vector2(key.x * TileSize - .5f, key.y * TileSize - .5f);
        private static bool Finite(Vector2 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x)
            && !float.IsNaN(point.y) && !float.IsInfinity(point.y);
    }
}
