using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Rendering
{
    internal sealed class SpriteMeshCache : IDisposable
    {
        private readonly Dictionary<Sprite, Mesh> meshes = new Dictionary<Sprite, Mesh>();
        internal Mesh Get(Sprite sprite)
        {
            if (sprite == null) return null;
            if (meshes.TryGetValue(sprite, out var mesh)) return mesh;
            Vector2[] source = sprite.vertices;
            var vertices = new Vector3[source.Length]; var normals = new Vector3[source.Length];
            var colors = new Color32[source.Length];
            for (int i = 0; i < source.Length; i++)
            { vertices[i] = source[i]; normals[i] = Vector3.back; colors[i] = new Color32(255, 255, 255, 255); }
            ushort[] indices = sprite.triangles; var triangles = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++) triangles[i] = indices[i];
            mesh = new Mesh { name = sprite.name + " (facility sprite)", vertices = vertices,
                normals = normals, colors32 = colors, uv = sprite.uv, triangles = triangles };
            mesh.RecalculateBounds(); meshes.Add(sprite, mesh); return mesh;
        }
        public void Dispose()
        {
            foreach (var mesh in meshes.Values) InstallationMaterialVariants.Destroy(mesh);
            meshes.Clear();
        }
    }
}
