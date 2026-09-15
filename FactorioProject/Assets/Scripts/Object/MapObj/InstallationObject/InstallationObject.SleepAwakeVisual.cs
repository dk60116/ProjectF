using System.Collections.Generic;
using UnityEngine;

public partial class InstallationObject
{
    private sealed class SleepAwakeRendererState
    {
        public Renderer Renderer;
        public readonly MaterialPropertyBlock OriginalPropertyBlock = new MaterialPropertyBlock();
        public readonly MaterialPropertyBlock DebugPropertyBlock = new MaterialPropertyBlock();
    }

    private readonly List<Renderer> sleepAwakeRendererScratch = new List<Renderer>();
    private readonly List<SleepAwakeRendererState> sleepAwakeRendererStates =
        new List<SleepAwakeRendererState>();
    private bool sleepAwakeDebugSleeping;
    private bool sleepAwakeDebugVisualApplied;

    public bool IsSleepAwakeDebugSleeping => sleepAwakeDebugSleeping;

    public static void RefreshAllSleepAwakeDebugVisuals()
    {
        foreach (InstallationObject installationObject in ActiveInstances)
        {
            installationObject?.RefreshSleepAwakeDebugVisual(true);
        }
    }

    protected void SetSleepAwakeDebugSleeping(bool sleeping)
    {
        if (sleepAwakeDebugSleeping == sleeping)
        {
            return;
        }

        sleepAwakeDebugSleeping = sleeping;
        RefreshSleepAwakeDebugVisual();
    }

    private void ResetSleepAwakeDebugVisual()
    {
        sleepAwakeDebugSleeping = false;
        RestoreSleepAwakeDebugVisual();
    }

    private void RefreshSleepAwakeDebugVisual(bool force = false)
    {
        bool shouldApply = sleepAwakeDebugSleeping
                           && Application.isPlaying
                           && isActiveAndEnabled
                           && GameManager.Instance != null
                           && GameManager.Instance.ShowSleepAwake;
        if (!force && sleepAwakeDebugVisualApplied == shouldApply)
        {
            return;
        }

        if (sleepAwakeDebugVisualApplied)
        {
            RestoreSleepAwakeDebugVisual();
        }

        if (!shouldApply)
        {
            return;
        }

        EnsureSleepAwakeRendererStates();
        for (int i = 0; i < sleepAwakeRendererStates.Count; i++)
        {
            SleepAwakeRendererState state = sleepAwakeRendererStates[i];
            Renderer renderer = state.Renderer;
            if (!IsSleepAwakeDebugRenderer(renderer))
            {
                continue;
            }

            renderer.GetPropertyBlock(state.OriginalPropertyBlock);
            renderer.GetPropertyBlock(state.DebugPropertyBlock);
            Material material = renderer.sharedMaterial;
            Color baseColor = ResolveCurrentSleepAwakeColor(state.DebugPropertyBlock, material);
            Color sleepingColor = SleepAwakeDebugVisual.Darken(baseColor);
            state.DebugPropertyBlock.SetColor(SleepAwakeDebugVisual.BaseColorPropertyId, sleepingColor);
            state.DebugPropertyBlock.SetColor(SleepAwakeDebugVisual.ColorPropertyId, sleepingColor);
            renderer.SetPropertyBlock(state.DebugPropertyBlock);
        }

        sleepAwakeDebugVisualApplied = true;
    }

    private void EnsureSleepAwakeRendererStates()
    {
        bool needsRefresh = sleepAwakeRendererStates.Count == 0;
        if (!needsRefresh)
        {
            for (int i = 0; i < sleepAwakeRendererStates.Count; i++)
            {
                if (sleepAwakeRendererStates[i].Renderer == null)
                {
                    needsRefresh = true;
                    break;
                }
            }
        }

        if (!needsRefresh)
        {
            return;
        }

        sleepAwakeRendererStates.Clear();
        sleepAwakeRendererScratch.Clear();
        GetComponentsInChildren(true, sleepAwakeRendererScratch);
        for (int i = 0; i < sleepAwakeRendererScratch.Count; i++)
        {
            Renderer renderer = sleepAwakeRendererScratch[i];
            if (IsSleepAwakeDebugRenderer(renderer))
            {
                sleepAwakeRendererStates.Add(new SleepAwakeRendererState { Renderer = renderer });
            }
        }

        sleepAwakeRendererScratch.Clear();
    }

    private void RestoreSleepAwakeDebugVisual()
    {
        if (!sleepAwakeDebugVisualApplied)
        {
            return;
        }

        for (int i = 0; i < sleepAwakeRendererStates.Count; i++)
        {
            SleepAwakeRendererState state = sleepAwakeRendererStates[i];
            if (state.Renderer != null)
            {
                state.Renderer.SetPropertyBlock(state.OriginalPropertyBlock);
            }
        }

        sleepAwakeDebugVisualApplied = false;
    }

    private static bool IsSleepAwakeDebugRenderer(Renderer renderer)
    {
        return renderer != null
               && renderer.sharedMaterial != null
               && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);
    }

    private static Color ResolveCurrentSleepAwakeColor(MaterialPropertyBlock block, Material material)
    {
        if (block != null)
        {
            if (block.HasColor(SleepAwakeDebugVisual.BaseColorPropertyId))
            {
                return block.GetColor(SleepAwakeDebugVisual.BaseColorPropertyId);
            }

            if (block.HasColor(SleepAwakeDebugVisual.ColorPropertyId))
            {
                return block.GetColor(SleepAwakeDebugVisual.ColorPropertyId);
            }
        }

        return SleepAwakeDebugVisual.GetMaterialBaseColor(material);
    }
}
