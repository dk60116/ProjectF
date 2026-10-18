using System;
using UnityEngine;

namespace ProjectF.Combat
{
    internal sealed class RangedAttackRangeView : IDisposable
    {
        private const int Segments = 96;
        private static readonly Vector3[] Directions = CreateDirections();
        private readonly GameObject root;
        private readonly LineRenderer line;
        private readonly Material material;
        private Vector3 shownCenter;
        private float shownRange = -1f;

        internal RangedAttackRangeView(Transform owner)
        {
            root = new GameObject("Ranged Attack Range");
            root.transform.SetParent(owner, false);
            line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = Segments;
            line.startWidth = line.endWidth = .045f;
            line.startColor = line.endColor = Color.white;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 5000;
            Shader shader = Shader.Find("Custom/MapFocusOverlay");
            if (shader != null)
            {
                material = new Material(shader) { name = "Ranged Attack Range", hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = 5000 };
                line.sharedMaterial = material;
            }
            line.enabled = false;
        }

        internal void Show(Vector3 center, float range)
        {
            if (line == null || material == null) return;
            if (range != shownRange || (center - shownCenter).sqrMagnitude > .000001f)
            {
                for (int i = 0; i < Segments; i++)
                    line.SetPosition(i, center + Directions[i] * range);
                shownCenter = center;
                shownRange = range;
            }
            line.enabled = true;
        }

        internal void Hide()
        {
            if (line != null) line.enabled = false;
        }

        public void Dispose()
        {
            Hide();
            UnityEngine.Object.Destroy(root);
            if (material != null) UnityEngine.Object.Destroy(material);
        }

        private static Vector3[] CreateDirections()
        {
            var result = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                result[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            return result;
        }
    }
}
