using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.Rendering
{
    // Property blocks are view inputs. Equivalent overrides share one material so
    // lamp textures and fluid colours do not split batches per installation.
    internal sealed class InstallationMaterialVariants : IDisposable
    {
        private struct Value : IEquatable<Value>
        {
            internal int Id;
            internal ShaderPropertyType Type;
            internal Vector4 Vector;
            internal Texture Texture;
            internal int Integer;
            public bool Equals(Value other) => Id == other.Id && Type == other.Type
                && Vector.Equals(other.Vector) && Texture == other.Texture && Integer == other.Integer;
        }
        private sealed class Variant
        {
            internal Value[] Values;
            internal Material Material;
        }
        private readonly Dictionary<Material, List<Variant>> variants = new Dictionary<Material, List<Variant>>();
        private readonly List<Value> scratch = new List<Value>(16);
        private MaterialPropertyBlock global, indexed;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        internal int Count { get; private set; }

        internal Material Resolve(Renderer renderer, Material source, int subMesh, SpriteRenderer sprite = null, Sprite iconOverride = null)
        {
            if (source == null || source.shader == null) return null;
            // Hosts construct this managed cache in field initializers. Native Unity objects
            // must wait until renderer submission on the main thread.
            global ??= new MaterialPropertyBlock();
            indexed ??= new MaterialPropertyBlock();
            scratch.Clear();
            global.Clear(); indexed.Clear();
            renderer.GetPropertyBlock(global);
            renderer.GetPropertyBlock(indexed, subMesh);
            // Unity uses an indexed block instead of the renderer-wide block.
            MaterialPropertyBlock block = indexed.isEmpty ? global : indexed;
            if (!block.isEmpty)
            {
                Shader shader = source.shader;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    int id = shader.GetPropertyNameId(i);
                    if (!block.HasProperty(id)) continue;
                    ShaderPropertyType type = shader.GetPropertyType(i);
                    var value = new Value { Id = id, Type = type };
                    switch (type)
                    {
                        case ShaderPropertyType.Color: value.Vector = block.GetColor(id); break;
                        case ShaderPropertyType.Vector: value.Vector = block.GetVector(id); break;
                        case ShaderPropertyType.Texture: value.Texture = block.GetTexture(id); break;
                        case ShaderPropertyType.Int: value.Integer = block.GetInt(id); break;
                        default: value.Vector.x = block.GetFloat(id); break;
                    }
                    scratch.Add(value);
                }
            }
            Sprite resolvedSprite = iconOverride != null ? iconOverride : sprite != null ? sprite.sprite : null;
            if (sprite != null && resolvedSprite != null)
            {
                if (source.HasProperty(MainTex)) Set(new Value { Id = MainTex,
                    Type = ShaderPropertyType.Texture, Texture = resolvedSprite.texture });
                if (source.HasProperty(ColorId))
                {
                    Color tint = source.GetColor(ColorId);
                    for (int i = 0; i < scratch.Count; i++)
                        if (scratch[i].Id == ColorId) { tint = scratch[i].Vector; break; }
                    Set(new Value { Id = ColorId, Type = ShaderPropertyType.Color,
                        Vector = tint * sprite.color });
                }
            }
            if (scratch.Count == 0)
            {
                source.enableInstancing = true;
                return source;
            }
            if (!variants.TryGetValue(source, out List<Variant> entries))
            {
                entries = new List<Variant>(2); variants.Add(source, entries);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                Value[] values = entries[i].Values;
                if (values.Length != scratch.Count) continue;
                bool matches = true;
                for (int j = 0; j < values.Length; j++)
                    if (!values[j].Equals(scratch[j])) { matches = false; break; }
                if (matches) return entries[i].Material;
            }
            var material = new Material(source) { name = source.name + " (installation variant)", enableInstancing = true };
            for (int i = 0; i < scratch.Count; i++)
            {
                Value value = scratch[i];
                switch (value.Type)
                {
                    case ShaderPropertyType.Color: material.SetColor(value.Id, value.Vector); break;
                    case ShaderPropertyType.Vector: material.SetVector(value.Id, value.Vector); break;
                    case ShaderPropertyType.Texture: material.SetTexture(value.Id, value.Texture); break;
                    case ShaderPropertyType.Int: material.SetInt(value.Id, value.Integer); break;
                    default: material.SetFloat(value.Id, value.Vector.x); break;
                }
            }
            entries.Add(new Variant { Values = scratch.ToArray(), Material = material }); Count++;
            return material;
        }
        private void Set(Value value)
        {
            for (int i = 0; i < scratch.Count; i++)
                if (scratch[i].Id == value.Id) { scratch[i] = value; return; }
            scratch.Add(value);
        }
        public void Dispose()
        {
            foreach (List<Variant> entries in variants.Values)
                for (int i = 0; i < entries.Count; i++) Destroy(entries[i].Material);
            variants.Clear(); Count = 0;
        }
        internal static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
