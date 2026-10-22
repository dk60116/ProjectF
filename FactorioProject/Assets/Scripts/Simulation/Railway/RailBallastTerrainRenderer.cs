using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    // Cached texture/property block per affected terrain chunk. The terrain draw
    // paints gravel into its own albedo; this class never submits geometry.
    internal sealed class RailBallastTerrainRenderer : System.IDisposable
    {
        private static readonly int MaskId = Shader.PropertyToID("_RailBallastMask");
        private static readonly int GravelId = Shader.PropertyToID("_RailBallastAlbedo");
        private static readonly int RectId = Shader.PropertyToID("_RailBallastRect");
        private static readonly int SamplingId = Shader.PropertyToID("_RailBallastSampling");
        private static readonly int EnabledId = Shader.PropertyToID("_RailBallastEnabled");
        private readonly RailBallastMask mask;
        private readonly Dictionary<Vector2Int, Entry> entries = new Dictionary<Vector2Int, Entry>();
        private Texture2D gravel;
        private sealed class Entry
        {
            internal Texture2D Texture;
            internal readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }

        internal RailBallastTerrainRenderer(int tileSize) { mask = new RailBallastMask(tileSize); }
        internal void Upsert(object key, IReadOnlyList<Vector2> path, RailVisualStyle style) => mask.Upsert(key, path, style.BallastHalfWidth);
        internal void Remove(object key) => mask.Remove(key);

        internal void Refresh()
        {
            if (!mask.Rebuild()) return;
            if (gravel == null) gravel = Resources.Load<Texture2D>("Textures/RailBallast");
            for (int i = 0; i < mask.Changed.Count; i++)
            {
                var tile = mask.Changed[i];
                if (tile.Sources.Count == 0)
                {
                    if (entries.TryGetValue(tile.Key, out Entry removed)) { DestroyTexture(removed.Texture); entries.Remove(tile.Key); }
                    continue;
                }
                if (!entries.TryGetValue(tile.Key, out Entry entry))
                {
                    entry = new Entry { Texture = new Texture2D(mask.PixelSize, mask.PixelSize, TextureFormat.R8, false, true)
                        { name = "Rail ballast terrain mask", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave } };
                    entries.Add(tile.Key, entry);
                    Vector2 origin = mask.Origin(tile.Key);
                    entry.Properties.SetVector(RectId, new Vector4(origin.x, origin.y, 1f / mask.TileSize, 1f / mask.TileSize));
                    float inner = (mask.PixelSize - 2f) / mask.PixelSize;
                    entry.Properties.SetVector(SamplingId, new Vector4(inner, 1f / mask.PixelSize, .25f, 0f));
                    entry.Properties.SetTexture(MaskId, entry.Texture);
                    entry.Properties.SetFloat(EnabledId, 1f);
                }
                entry.Properties.SetTexture(GravelId, gravel);
                entry.Texture.SetPixelData(tile.Pixels, 0);
                entry.Texture.Apply(false, false);
            }
        }

        internal MaterialPropertyBlock GetProperties(Vector2Int chunk) => gravel != null && entries.TryGetValue(chunk, out Entry entry) ? entry.Properties : null;
        public void Dispose()
        {
            foreach (Entry entry in entries.Values) DestroyTexture(entry.Texture);
            entries.Clear();
        }
        private static void DestroyTexture(Texture2D texture)
        { if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture); }
    }
}
