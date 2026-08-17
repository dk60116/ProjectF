using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class InstallationGridOverlayCleanup
{
    private static readonly List<GameObject> SceneRoots = new List<GameObject>();
    private static readonly List<GameObject> OverlayCandidates = new List<GameObject>();
    private static readonly List<Transform> TransformTraversal = new List<Transform>();

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
        if (state == PlayModeStateChange.ExitingPlayMode
            || state == PlayModeStateChange.EnteredEditMode)
        {
            CleanupInEditMode();
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
        if (targetScene.IsValid())
        {
            CollectRuntimeGridOverlays(targetScene);
        }
        else
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                CollectRuntimeGridOverlays(SceneManager.GetSceneAt(sceneIndex));
            }
        }

        bool removedOverlay = false;
        for (int i = OverlayCandidates.Count - 1; i >= 0; i--)
        {
            GameObject candidate = OverlayCandidates[i];
            if (!IsLoadedSceneObject(candidate, targetScene))
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

    private static void CollectRuntimeGridOverlays(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        SceneRoots.Clear();
        TransformTraversal.Clear();
        scene.GetRootGameObjects(SceneRoots);
        for (int i = 0; i < SceneRoots.Count; i++)
        {
            GameObject root = SceneRoots[i];
            if (root != null)
            {
                TransformTraversal.Add(root.transform);
            }
        }

        while (TransformTraversal.Count > 0)
        {
            int lastIndex = TransformTraversal.Count - 1;
            Transform current = TransformTraversal[lastIndex];
            TransformTraversal.RemoveAt(lastIndex);
            if (current == null)
            {
                continue;
            }

            GameObject candidate = current.gameObject;
            if (IsRuntimeGridOverlay(candidate))
            {
                OverlayCandidates.Add(candidate);
                continue;
            }

            for (int childIndex = 0; childIndex < current.childCount; childIndex++)
            {
                TransformTraversal.Add(current.GetChild(childIndex));
            }
        }

        SceneRoots.Clear();
        TransformTraversal.Clear();
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
