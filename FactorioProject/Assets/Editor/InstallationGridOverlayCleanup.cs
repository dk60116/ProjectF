using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class InstallationGridOverlayCleanup
{
    private static readonly List<GameObject> OverlayCandidates = new List<GameObject>();

    static InstallationGridOverlayCleanup()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        EditorSceneManager.sceneSaving -= HandleSceneSaving;
        EditorSceneManager.sceneSaving += HandleSceneSaving;
        EditorApplication.delayCall -= CleanupInEditMode;
        EditorApplication.delayCall += CleanupInEditMode;
    }

    [MenuItem("Tools/ProjectF/Cleanup Transient Editor Objects")]
    private static void CleanupLoadedScenesFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        CleanupInEditMode();
        EditorSceneManager.MarkAllScenesDirty();
        SceneView.RepaintAll();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            ScheduleCleanupAfterPlayMode();
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode)
        {
            CleanupAfterPlayMode();
            // Unity restores Scene view draw settings at the end of the play-mode
            // transition, so apply the grid state once more on the next editor tick.
            ScheduleCleanupAfterPlayMode();
        }
    }

    private static void ScheduleCleanupAfterPlayMode()
    {
        EditorApplication.delayCall -= CleanupAfterPlayMode;
        EditorApplication.delayCall += CleanupAfterPlayMode;
    }

    private static void CleanupAfterPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        CleanupInEditMode();
        HideSceneViewGrids();
    }

    private static void HideSceneViewGrids()
    {
        foreach (SceneView sceneView in SceneView.sceneViews)
        {
            if (sceneView == null || !sceneView.showGrid)
            {
                continue;
            }

            sceneView.showGrid = false;
            sceneView.Repaint();
        }
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        // Scene deserialization must finish before transient hierarchies are removed.
        EditorApplication.delayCall -= CleanupInEditMode;
        EditorApplication.delayCall += CleanupInEditMode;
    }

    private static void HandleSceneSaving(Scene scene, string path)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        bool removedTerrainPreview = CleanupTerrainPreviewChunks(scene, false);
        bool removedOverlay = CleanupOrphanedOverlays(scene, false);
        if (removedTerrainPreview || removedOverlay)
        {
            SceneView.RepaintAll();
        }
    }

    private static void CleanupInEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        bool removedTerrainPreview = CleanupTerrainPreviewChunks(default, true);
        bool removedOverlay = CleanupOrphanedOverlays(default, true);
        if (removedTerrainPreview || removedOverlay)
        {
            SceneView.RepaintAll();
        }
    }

    private static bool CleanupTerrainPreviewChunks(Scene targetScene, bool markSceneDirty)
    {
        TerrainGenerator[] generators = Object.FindObjectsByType<TerrainGenerator>(FindObjectsInactive.Include);
        bool removedPreview = false;
        for (int i = 0; i < generators.Length; i++)
        {
            TerrainGenerator generator = generators[i];
            if (!IsLoadedSceneObject(generator, targetScene)
                || !generator.HasEditorPreviewChunks())
            {
                continue;
            }

            Scene previewScene = generator.gameObject.scene;
            generator.ClearEditorPreviewChunks();
            if (markSceneDirty && previewScene.IsValid() && previewScene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(previewScene);
            }

            removedPreview = true;
        }

        return removedPreview;
    }

    private static bool CleanupOrphanedOverlays(Scene targetScene, bool markSceneDirty)
    {
        OverlayCandidates.Clear();
        CollectRuntimeGridOverlays(targetScene);

        bool removedOverlay = false;
        for (int i = OverlayCandidates.Count - 1; i >= 0; i--)
        {
            GameObject candidate = OverlayCandidates[i];
            if (candidate == null)
            {
                continue;
            }

            Scene overlayScene = candidate.scene;
            UnityEngine.Object.DestroyImmediate(candidate);
            if (markSceneDirty && overlayScene.IsValid() && overlayScene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(overlayScene);
            }

            removedOverlay = true;
        }

        OverlayCandidates.Clear();
        return removedOverlay;
    }

    private static void CollectRuntimeGridOverlays(Scene targetScene)
    {
        // Runtime overlays use HideAndDontSave, and can temporarily live outside
        // SceneManager.sceneCount while play mode is shutting down. Scan editor
        // objects directly so those hidden orphans cannot survive the transition.
        GameObject[] candidates = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < candidates.Length; i++)
        {
            GameObject candidate = candidates[i];
            if (!IsRuntimeGridOverlay(candidate)
                || EditorUtility.IsPersistent(candidate))
            {
                continue;
            }

            Scene candidateScene = candidate.scene;
            if (targetScene.IsValid()
                && (!candidateScene.IsValid()
                    || !candidateScene.isLoaded
                    || candidateScene != targetScene))
            {
                continue;
            }

            OverlayCandidates.Add(candidate);
        }
    }

    private static bool IsLoadedSceneObject(Component candidate, Scene targetScene)
    {
        return candidate != null && IsLoadedSceneObject(candidate.gameObject, targetScene);
    }

    private static bool IsLoadedSceneObject(GameObject candidate, Scene targetScene)
    {
        if (candidate == null || EditorUtility.IsPersistent(candidate))
        {
            return false;
        }

        Scene candidateScene = candidate.scene;
        return candidateScene.IsValid()
               && candidateScene.isLoaded
               && (!targetScene.IsValid() || candidateScene == targetScene);
    }

    private static bool IsRuntimeGridOverlay(GameObject candidate)
    {
        if (candidate == null
            || (candidate.hideFlags & HideFlags.DontSave) == 0)
        {
            return false;
        }

        return candidate.name == InstallationPlacementController.InstallGridOverlayObjectName
               || candidate.name == InstallationPlacementController.InstallGridPreviewOverlayObjectName;
    }
}
