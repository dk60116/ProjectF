using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Rendering
{
    // One camera decision and one visual loop, after camera movement and before batch rendering.
    [DefaultExecutionOrder(850), DisallowMultipleComponent]
    public sealed class WorldVisualUpdateManager : MonoBehaviour
    {
        private static WorldVisualUpdateManager instance;
        private readonly List<InstallationVisualState> targets = new List<InstallationVisualState>();
        private readonly CameraRenderCulling culling = new CameraRenderCulling();

        public int RegisteredCount => targets.Count;
        public int VisibleCount { get; private set; }
        public int CulledCount { get; private set; }

        internal static void Register(InstallationVisualState target)
        {
            if (!Application.isPlaying || target.Index >= 0)
                return;
            if (instance == null)
            {
                var host = new GameObject(nameof(WorldVisualUpdateManager));
                instance = host.AddComponent<WorldVisualUpdateManager>();
                DontDestroyOnLoad(host);
            }
            target.Index = instance.targets.Count;
            instance.targets.Add(target);
        }

        internal static void Unregister(InstallationVisualState target)
        {
            if (target == null)
                return;
            if (instance != null && target.Index >= 0)
            {
                int index = target.Index;
                int last = instance.targets.Count - 1;
                InstallationVisualState moved = instance.targets[last];
                instance.targets[index] = moved;
                moved.Index = index;
                instance.targets.RemoveAt(last);
            }
            target.Index = -1;
            target.Release();
        }

        private void LateUpdate()
        {
            culling.Update(Camera.main);
            VisibleCount = 0;
            CulledCount = 0;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                InstallationVisualState target = targets[i];
                if (target.Owner == null || !target.Owner.isActiveAndEnabled)
                {
                    Unregister(target);
                    continue;
                }
                target.Tick(culling, Time.deltaTime);
                if (target.Visible) VisibleCount++;
                else CulledCount++;
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < targets.Count; i++)
                targets[i].SetVisible(true);
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Index = -1;
                targets[i].Release();
            }
            targets.Clear();
            instance = null;
        }
    }
}

