using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.MapObjects
{
    // Presentation only. The store owns HP and wreckage; no MonoBehaviour is added to a placement.
    internal sealed class MapObjectHealthPresentation : IDisposable
    {
        private readonly TerrainGenerator terrain;
        private readonly BlockStateStore store;
        private readonly GameObject root;
        private readonly Dictionary<Vector2Int, Bar> bars = new();
        private readonly Stack<Bar> pool = new();
        private readonly Dictionary<long, Wreck> wrecks = new();
        private readonly List<Vector2Int> removedBars = new();
        private readonly List<long> removedWrecks = new();
        private readonly HashSet<long> activeWrecks = new();
        private readonly Dictionary<int, float> heights = new();
        private Camera camera;
        private int wreckRevision = -1;

        internal MapObjectHealthPresentation(TerrainGenerator terrain, BlockStateStore store)
        {
            this.terrain = terrain;
            this.store = store;
            root = new GameObject("Map Object Health Presentation");
            root.transform.SetParent(terrain.transform, false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        internal void Update()
        {
            if (camera == null || !camera.isActiveAndEnabled) camera = Camera.main;
            removedBars.Clear();
            foreach (var pair in bars)
                if (!store.DamagedInstallations.ContainsKey(pair.Key)) removedBars.Add(pair.Key);
            for (int i = 0; i < removedBars.Count; i++)
            {
                var bar = bars[removedBars[i]];
                bar.Root.gameObject.SetActive(false);
                bars.Remove(removedBars[i]);
                pool.Push(bar);
            }

            foreach (var pair in store.DamagedInstallations)
            {
                var state = pair.Value;
                Vector3 position = terrain.GetHealthBarPosition(state);
                bool loaded = terrain.TryGetLoadedBlock(TerrainGenerator.GetWorldBlockCoordinate(position), out _);
                ItemDefinition definition = null;
                GameManager.Instance?.ItemManger?.TryGetItemDefinitionById(state.itemId, out definition);
                float maximum = definition != null ? definition.MaximumHealth : 100f;
                bool visible = loaded && camera != null && state.combatHealth > 0f && state.combatHealth < maximum;
                position.y += GetHeight(definition);
                if (visible)
                {
                    Vector3 screen = camera.WorldToViewportPoint(position);
                    visible = screen.z > 0f && screen.x >= -0.05f && screen.x <= 1.05f && screen.y >= -0.05f && screen.y <= 1.05f;
                }
                if (!bars.TryGetValue(pair.Key, out var bar))
                {
                    if (!visible) continue;
                    bar = pool.Count > 0 ? pool.Pop() : new Bar(root.transform);
                    bars.Add(pair.Key, bar);
                }
                bar.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                bar.Root.SetPositionAndRotation(position, camera.transform.rotation);
                bar.Fill.anchorMax = new Vector2(0.04f + 0.92f * Mathf.Clamp01(state.combatHealth / maximum), 0.82f);
            }

            if (wreckRevision != store.WreckageRevision) SyncWreckage();
            foreach (var wreck in wrecks.Values)
                wreck.Root.SetActive(terrain.TryGetLoadedBlock(wreck.State.anchorCoordinate, out _));
        }

        private float GetHeight(ItemDefinition definition)
        {
            if (definition == null || definition.mapObject == null) return 1f;
            if (heights.TryGetValue(definition.id, out float height)) return height;
            height = 0.5f;
            foreach (var renderer in definition.mapObject.GetComponentsInChildren<MeshRenderer>(true))
                height = Mathf.Max(height, renderer.bounds.max.y - definition.mapObject.transform.position.y + 0.25f);
            heights.Add(definition.id, height);
            return height;
        }

        private void SyncWreckage()
        {
            activeWrecks.Clear();
            for (int i = 0; i < store.Wreckage.Count; i++)
            {
                var state = store.Wreckage[i];
                activeWrecks.Add(state.placementSequence);
                if (wrecks.TryGetValue(state.placementSequence, out var existing) && !ReferenceEquals(existing.State, state))
                {
                    Destroy(existing.Root);
                    wrecks.Remove(state.placementSequence);
                }
                if (!wrecks.ContainsKey(state.placementSequence))
                    wrecks.Add(state.placementSequence, new Wreck(root.transform, state, terrain.ResolveWreckagePrototype(state)));
            }
            removedWrecks.Clear();
            foreach (var pair in wrecks)
                if (!activeWrecks.Contains(pair.Key)) removedWrecks.Add(pair.Key);
            for (int i = 0; i < removedWrecks.Count; i++)
            {
                Destroy(wrecks[removedWrecks[i]].Root);
                wrecks.Remove(removedWrecks[i]);
            }
            wreckRevision = store.WreckageRevision;
        }

        internal bool TryRaycastWreckage(Ray ray, out long sequence)
        {
            sequence = 0L;
            float nearest = float.MaxValue;
            foreach (var pair in wrecks)
            {
                var wreck = pair.Value;
                if (!wreck.Root.activeSelf || !wreck.Bounds.IntersectRay(ray, out float distance) || distance >= nearest) continue;
                nearest = distance;
                sequence = pair.Key;
            }
            return sequence > 0L;
        }

        public void Dispose() => Destroy(root);
        private static void Destroy(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private sealed class Bar
        {
            internal readonly RectTransform Root, Fill;
            internal Bar(Transform parent)
            {
                var go = new GameObject("Damaged Object HP", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
                Root = (RectTransform)go.transform;
                Root.SetParent(parent, false);
                Root.sizeDelta = new Vector2(0.9f, 0.1f);
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 100;
                var background = go.GetComponent<UnityEngine.UI.Image>();
                background.color = new Color(0.04f, 0.04f, 0.04f, 0.85f);
                background.raycastTarget = false;
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                Fill = (RectTransform)fill.transform;
                Fill.SetParent(Root, false);
                Fill.anchorMin = new Vector2(0.04f, 0.18f);
                Fill.anchorMax = new Vector2(0.96f, 0.82f);
                Fill.offsetMin = Fill.offsetMax = Vector2.zero;
                var image = fill.GetComponent<UnityEngine.UI.Image>();
                image.color = new Color(0.78f, 0.12f, 0.1f);
                image.raycastTarget = false;
            }
        }

        private sealed class Wreck
        {
            internal readonly GameObject Root;
            internal readonly BlockStateStore.InstallationSaveState State;
            internal Bounds Bounds;
            internal Wreck(Transform parent, BlockStateStore.InstallationSaveState state, MapObject prototype)
            {
                State = state;
                Root = new GameObject("Wreckage");
                Root.transform.SetParent(parent, false);
                Root.transform.SetPositionAndRotation(state.worldPosition, state.worldRotation);
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", new Color(0.3f, 0.3f, 0.3f));
                tint.SetColor("_Color", new Color(0.3f, 0.3f, 0.3f));
                tint.SetColor("_EmissionColor", Color.black);
                if (prototype != null)
                {
                    foreach (var source in prototype.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!source.enabled || !source.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                        var matrix = prototype.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
                        AddPart(filter.sharedMesh, source.sharedMaterials, matrix, tint);
                    }
                }
                if (Root.transform.childCount == 0)
                {
                    ItemDefinition definition = null;
                    GameManager.Instance?.ItemManger?.TryGetItemDefinitionById(state.itemId, out definition);
                    if (definition?.portableMesh != null && definition.portableMat != null)
                        AddPart(definition.portableMesh, new[] { definition.portableMat }, Matrix4x4.identity, tint);
                }
                Vector3 scale = prototype != null ? prototype.transform.localScale : Vector3.one;
                Root.transform.localScale = Vector3.Scale(scale, new Vector3(1f, 0.2f, 1f));
                Bounds = new Bounds(state.worldPosition + Vector3.up * 0.15f, new Vector3(0.8f, 0.3f, 0.8f));
                foreach (var renderer in Root.GetComponentsInChildren<MeshRenderer>()) Bounds.Encapsulate(renderer.bounds);
            }

            private void AddPart(Mesh mesh, Material[] materials, Matrix4x4 local, MaterialPropertyBlock tint)
            {
                var part = new GameObject("Debris", typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(Root.transform, false);
                part.transform.localPosition = local.GetColumn(3);
                part.transform.localRotation = local.rotation;
                part.transform.localScale = local.lossyScale;
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.SetPropertyBlock(tint);
            }
        }
    }
}
