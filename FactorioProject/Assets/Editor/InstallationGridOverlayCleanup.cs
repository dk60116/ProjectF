using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class InstallationGridOverlayCleanup
{
    static InstallationGridOverlayCleanup()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.delayCall += CleanupInEditMode;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode
            || state == PlayModeStateChange.EnteredEditMode)
        {
            CleanupOrphanedOverlays();
        }
    }

    private static void CleanupInEditMode()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            CleanupOrphanedOverlays();
        }
    }

    private static void CleanupOrphanedOverlays()
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        bool removedOverlay = false;
        for (int i = objects.Length - 1; i >= 0; i--)
        {
            GameObject candidate = objects[i];
            if (!IsRuntimeGridOverlay(candidate))
            {
                continue;
            }

            Object.DestroyImmediate(candidate);
            removedOverlay = true;
        }

        if (removedOverlay)
        {
            SceneView.RepaintAll();
        }
    }

    private static bool IsRuntimeGridOverlay(GameObject candidate)
    {
        if (candidate == null
            || EditorUtility.IsPersistent(candidate)
            || (candidate.hideFlags & HideFlags.DontSave) == 0)
        {
            return false;
        }

        return candidate.name == InstallationPlacementController.InstallGridOverlayObjectName
               || candidate.name == InstallationPlacementController.InstallGridPreviewOverlayObjectName;
    }
}
