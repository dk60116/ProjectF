using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectF.Editor.Diagnostics
{
    [InitializeOnLoad]
    internal static class BeltAlignmentProbe
    {
        private const string CornerPrefabPath = "Assets/MapObject/Belt/Conveyor belt/Conveyor belt_Corner.prefab";
        private const string StraightPrefabPath = "Assets/MapObject/Belt/Conveyor belt/Conveyor belt.prefab";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string RequestPath => Path.Combine(ProjectRoot, "Library", "CodexBeltAlignment.request");
        private static string OutputPath => Path.Combine(ProjectRoot, "Library", "CodexBeltAlignment.png");

        static BeltAlignmentProbe()
        {
            EditorApplication.delayCall += RunWhenRequested;
        }

        [MenuItem("Tools/Diagnostics/Render Belt Alignment")]
        private static void RunFromMenu()
        {
            Render();
        }

        public static void RunBatch()
        {
            Render();
        }

        private static void RunWhenRequested()
        {
            if (!File.Exists(RequestPath))
            {
                return;
            }

            File.Delete(RequestPath);
            Render();
        }

        private static void Render()
        {
            GameObject cornerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CornerPrefabPath);
            GameObject straightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StraightPrefabPath);
            if (cornerPrefab == null || straightPrefab == null)
            {
                Debug.LogError("[BeltAlignmentProbe] Required conveyor prefabs were not found.");
                return;
            }

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D image = null;

            try
            {
                Instantiate(cornerPrefab, previewScene, Vector3.zero, Quaternion.identity);
                Instantiate(straightPrefab, previewScene, new Vector3(0f, 0f, 1f), Quaternion.identity);
                Instantiate(straightPrefab, previewScene, new Vector3(0f, 0f, 2f), Quaternion.identity);
                Instantiate(straightPrefab, previewScene, new Vector3(-1f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                Instantiate(straightPrefab, previewScene, new Vector3(-2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

                GameObject cameraObject = new GameObject("Belt Alignment Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.SetPositionAndRotation(new Vector3(-0.5f, 10f, 0.5f), Quaternion.Euler(90f, 0f, 0f));
                camera.orthographic = true;
                camera.orthographicSize = 1.85f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.35f, 0.33f, 0.31f, 1f);
                camera.allowHDR = false;
                camera.allowMSAA = false;

                GameObject lightObject = new GameObject("Belt Alignment Light");
                SceneManager.MoveGameObjectToScene(lightObject, previewScene);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1
                };
                camera.targetTexture = target;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                image = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, 1024f, 1024f), 0, 0);
                image.Apply(false, false);
                RenderTexture.active = previous;

                File.WriteAllBytes(OutputPath, image.EncodeToPNG());
                Debug.Log($"[BeltAlignmentProbe] Rendered {OutputPath}");
            }
            finally
            {
                if (image != null)
                {
                    Object.DestroyImmediate(image);
                }

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static void Instantiate(GameObject prefab, Scene scene, Vector3 position, Quaternion rotation)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
