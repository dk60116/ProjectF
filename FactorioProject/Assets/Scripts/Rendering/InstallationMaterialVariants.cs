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
            internal Shader Shader;
        }
        private sealed class ShaderInfo
        {
            internal bool SupportsInstancing;
            internal Value[] Properties;
        }
        private readonly Dictionary<Material, List<Variant>> variants = new Dictionary<Material, List<Variant>>();
        private readonly Dictionary<Shader, ShaderInfo> shaders = new Dictionary<Shader, ShaderInfo>();
        private readonly List<Value> scratch = new List<Value>(16);
        private MaterialPropertyBlock global, indexed;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        internal int Count { get; private set; }
        internal long ShaderCacheHits { get; private set; }
        internal long ShaderCacheMisses { get; private set; }
        internal long PropertyLayoutBuilds { get; private set; }
        internal long EmptyPropertyBlockSkips { get; private set; }

        private ShaderInfo GetShaderInfo(Shader shader)
        {
            if (shaders.TryGetValue(shader, out ShaderInfo info))
            {
                ShaderCacheHits++;
                return info;
            }
            ShaderCacheMisses++;
            info = new ShaderInfo { SupportsInstancing = shader.keywordSpace.FindKeyword("INSTANCING_ON").isValid };
            shaders.Add(shader, info);
            return info;
        }

        internal bool SupportsInstancing(Shader shader) => shader != null && GetShaderInfo(shader).SupportsInstancing;

        private Value[] GetShaderProperties(Shader shader)
        {
            ShaderInfo info = GetShaderInfo(shader);
            if (info.Properties != null) return info.Properties;
            var properties = new Value[shader.GetPropertyCount()];
            for (int i = 0; i < properties.Length; i++)
                properties[i] = new Value { Id = shader.GetPropertyNameId(i), Type = shader.GetPropertyType(i) };
            info.Properties = properties;
            PropertyLayoutBuilds++;
            return properties;
        }

        internal Material Resolve(Renderer renderer, Material source, int subMesh, SpriteRenderer sprite = null, Sprite iconOverride = null)
        {
            if (source == null) return null;
            Shader shader = source.shader;
            if (shader == null) return null;
            scratch.Clear();
            // Keep runtime overrides observable, but avoid native block copies for plain parts.
            if (renderer.HasPropertyBlock())
            {
                global ??= new MaterialPropertyBlock();
                indexed ??= new MaterialPropertyBlock();
                global.Clear(); indexed.Clear();
                renderer.GetPropertyBlock(indexed, subMesh);
                MaterialPropertyBlock block = indexed;
                if (indexed.isEmpty) { renderer.GetPropertyBlock(global); block = global; }
                Value[] properties = GetShaderProperties(shader);
                for (int i = 0; i < properties.Length; i++)
                {
                    Value value = properties[i];
                    int id = value.Id;
                    if (!block.HasProperty(id)) continue;
                    switch (value.Type)
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
            else EmptyPropertyBlockSkips++;
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
                if (!source.enableInstancing) source.enableInstancing = true;
                return source;
            }
            if (!variants.TryGetValue(source, out List<Variant> entries))
            {
                entries = new List<Variant>(2); variants.Add(source, entries);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Shader != shader || entries[i].Material == null) continue;
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
            entries.Add(new Variant { Values = scratch.ToArray(), Material = material, Shader = shader }); Count++;
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
            variants.Clear(); shaders.Clear(); Count = 0;
        }
        internal static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
