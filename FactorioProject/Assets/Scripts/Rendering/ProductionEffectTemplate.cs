using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.Rendering
{
    // Visible effects sample immutable emitter data; facilities own no particle simulation or GameObject.
    internal sealed class ProductionEffectTemplate : IDisposable
    {
        private readonly Matrix4x4 local;
        private readonly ParticleSystem.MinMaxCurve lifetime, speed, sizeX, sizeY, sizeZ;
        private readonly ParticleSystem.MinMaxCurve sizeOverLifetime;
        private readonly ParticleSystem.MinMaxGradient startColor, colorOverLifetime;
        private readonly ParticleSystem.Burst[] bursts;
        private readonly float emissionRate, gravity, radius, duration, maximumLifetime, sheetFps;
        private readonly Vector3 velocity, pivot;
        private readonly int layer, tilesX, tilesY, maxParticles;
        private readonly bool sizeEnabled, colorEnabled, looping, sheetUsesFps;
        private readonly ParticleSystemRenderMode renderMode;
        private readonly Material material;
        private readonly Mesh[,] meshes;

        internal ProductionEffectTemplate(ParticleSystem source, Transform root)
        {
            var main = source.main; var emission = source.emission; var shape = source.shape;
            var renderer = source.GetComponent<ParticleSystemRenderer>();
            local = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
            lifetime = main.startLifetime; speed = main.startSpeed;
            sizeX = main.startSize3D ? main.startSizeX : main.startSize;
            sizeY = main.startSize3D ? main.startSizeY : main.startSize;
            sizeZ = main.startSize3D ? main.startSizeZ : main.startSize;
            startColor = main.startColor;
            emissionRate = emission.enabled ? Mathf.Max(0, emission.rateOverTime.constantMax) : 0;
            bursts = emission.enabled ? new ParticleSystem.Burst[emission.burstCount] : Array.Empty<ParticleSystem.Burst>();
            if (bursts.Length > 0) emission.GetBursts(bursts);
            duration = Mathf.Max(.001f, main.duration); looping = main.loop;
            maximumLifetime = Mathf.Max(0, lifetime.constantMax);
            gravity = main.gravityModifier.constantMax;
            radius = shape.enabled ? shape.radius : 0;
            var velocityModule = source.velocityOverLifetime;
            velocity = velocityModule.enabled
                ? new Vector3(velocityModule.x.constantMax, velocityModule.y.constantMax, velocityModule.z.constantMax)
                : Vector3.zero;
            var sizes = source.sizeOverLifetime; sizeEnabled = sizes.enabled; sizeOverLifetime = sizes.size;
            var colors = source.colorOverLifetime; colorEnabled = colors.enabled; colorOverLifetime = colors.color;
            var sheet = source.textureSheetAnimation;
            tilesX = sheet.enabled ? Mathf.Max(1, sheet.numTilesX) : 1;
            tilesY = sheet.enabled ? Mathf.Max(1, sheet.numTilesY) : 1;
            sheetUsesFps = sheet.enabled && sheet.timeMode == ParticleSystemAnimationTimeMode.FPS;
            sheetFps = sheet.fps;
            layer = source.gameObject.layer; maxParticles = Math.Max(0, main.maxParticles);
            if (renderer != null)
            {
                renderMode = renderer.renderMode; pivot = renderer.pivot;
                if (renderer.enabled && renderer.sharedMaterial != null && (emissionRate > 0 || bursts.Length > 0))
                    material = new Material(renderer.sharedMaterial) {
                        name = renderer.sharedMaterial.name + " (shared production effect)", enableInstancing = true };
            }
            meshes = new Mesh[Math.Min(256, tilesX * tilesY), 16];
        }

        internal void Append(ProductionFacilityInstance facility, Matrix4x4 root, Camera camera, VirtualRenderBatchCollection batches)
        {
            if (material == null || maximumLifetime <= 0) return;
            double phase = facility.AnimationPhase;
            Matrix4x4 emitter = root * local;
            int remaining = maxParticles;
            if (emissionRate > 0)
            {
                long latest = (long)Math.Floor(phase * emissionRate);
                int count = Math.Min(maxParticles, Mathf.CeilToInt(emissionRate * maximumLifetime));
                for (int i = 0; i < count; i++)
                {
                    long ordinal = latest - i;
                    if (ordinal < 0) break;
                    AppendParticle(facility, emitter, camera, batches, ordinal,
                        (float)(phase - ordinal / (double)emissionRate), ref remaining);
                }
            }
            for (int b = 0; b < bursts.Length && remaining > 0; b++)
            {
                var burst = bursts[b];
                int cycles = Math.Max(1, burst.cycleCount);
                double span = (cycles - 1) * (double)burst.repeatInterval;
                GetBurstLoopRange(phase, maximumLifetime, duration, looping, burst.time, span,
                    out long firstLoop, out long lastLoop);
                for (long loop = lastLoop; loop >= firstLoop && remaining > 0; loop--)
                {
                    double start = loop * (double)duration + burst.time;
                    int lastCycle = burst.repeatInterval > 0
                        ? Math.Min(cycles - 1, (int)Math.Floor((phase - start) / burst.repeatInterval)) : 0;
                    for (int cycle = lastCycle; cycle >= 0 && remaining > 0; cycle--)
                    {
                        float age = (float)(phase - start - cycle * (double)burst.repeatInterval);
                        if (age >= maximumLifetime) break;
                        uint seed = unchecked((uint)(loop * 397 + cycle * 17 + b * 7919));
                        if (Fraction(seed ^ (uint)facility.SimulationId) >= burst.probability) continue;
                        int count = Math.Min(remaining, Mathf.CeilToInt(burst.count.Evaluate(0, Fraction(seed))));
                        for (int i = 0; i < count; i++)
                            AppendParticle(facility, emitter, camera, batches, unchecked(seed * 31u + (uint)i), age, ref remaining);
                    }
                }
            }
        }

        // Evaluate only loops whose emitted particles can still be alive, including large saved phases.
        internal static void GetBurstLoopRange(double phase, float lifetime, float duration, bool looping,
            float time, double span, out long first, out long last)
        {
            first = looping ? Math.Max(0L, (long)Math.Ceiling((phase - lifetime - time - span) / duration)) : 0;
            last = looping ? (long)Math.Floor((phase - time) / duration) : (phase >= time ? 0 : -1);
        }

        private void AppendParticle(ProductionFacilityInstance facility, Matrix4x4 emitter, Camera camera,
            VirtualRenderBatchCollection batches, long ordinal, float age, ref int remaining)
        {
            float random = Fraction(unchecked((uint)(ordinal ^ facility.SimulationId)));
            float particleLifetime = lifetime.Evaluate(0, random);
            if (age < 0 || age >= particleLifetime || particleLifetime <= 0) return;
            float normalized = age / particleLifetime;
            float angle = Fraction(unchecked((uint)ordinal + 17u)) * Mathf.PI * 2;
            Vector3 origin = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * (radius * random);
            Vector3 motion = (Vector3.forward * speed.Evaluate(0, random) + velocity) * age;
            Vector3 position = emitter.MultiplyPoint3x4(origin + motion)
                + Physics.gravity * (gravity * age * age * .5f);
            float growth = sizeEnabled ? sizeOverLifetime.Evaluate(normalized, random) : 1;
            Vector3 particleSize = new Vector3(sizeX.Evaluate(0, random), sizeY.Evaluate(0, random), sizeZ.Evaluate(0, random)) * growth;
            int frameCount = meshes.GetLength(0);
            int frame = sheetUsesFps ? Mathf.FloorToInt(age * sheetFps) % frameCount
                : Math.Min(frameCount - 1, Mathf.FloorToInt(normalized * frameCount));
            int colorStep = Mathf.Clamp(Mathf.FloorToInt(normalized * 16), 0, 15);
            Mesh mesh = GetMesh(frame, colorStep);
            Quaternion rotation = camera.transform.rotation;
            if (renderMode == ParticleSystemRenderMode.VerticalBillboard)
            {
                Vector3 facing = camera.transform.forward; facing.y = 0;
                if (facing.sqrMagnitude > .0001f) rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.Scale(emitter.lossyScale, particleSize));
            batches.AddMatrix(new VirtualRenderBatchKey(mesh, material, layer, 0,
                ShadowCastingMode.Off, false, false,
                batchCellX: Mathf.FloorToInt(position.x / 16), batchCellZ: Mathf.FloorToInt(position.z / 16)), matrix);
            remaining--;
        }

        private static float Fraction(uint seed)
        {
            seed ^= seed >> 16; seed *= 0x7feb352du; seed ^= seed >> 15; seed *= 0x846ca68bu;
            seed ^= seed >> 16; return (seed & 0xffffff) / 16777216f;
        }
        private Mesh GetMesh(int frame, int step)
        {
            if (meshes[frame, step] != null) return meshes[frame, step];
            float x = frame % tilesX / (float)tilesX, y = 1f - (frame / tilesX + 1f) / tilesY;
            float w = 1f / tilesX, h = 1f / tilesY;
            Color color = startColor.Evaluate(0, .5f)
                * (colorEnabled ? colorOverLifetime.Evaluate((step + .5f) / 16, .5f) : Color.white);
            var mesh = new Mesh {
                name = "Production effect billboard",
                vertices = new[] {
                    new Vector3(-.5f,-.5f,0) + pivot, new Vector3(.5f,-.5f,0) + pivot,
                    new Vector3(.5f,.5f,0) + pivot, new Vector3(-.5f,.5f,0) + pivot },
                uv = new[] { new Vector2(x,y), new Vector2(x+w,y), new Vector2(x+w,y+h), new Vector2(x,y+h) },
                colors = new[] { color, color, color, color }, triangles = new[] { 0,2,1,0,3,2 } };
            mesh.RecalculateBounds(); meshes[frame, step] = mesh; return mesh;
        }
        public void Dispose()
        {
            InstallationMaterialVariants.Destroy(material);
            foreach (var mesh in meshes) InstallationMaterialVariants.Destroy(mesh);
        }
    }
}
