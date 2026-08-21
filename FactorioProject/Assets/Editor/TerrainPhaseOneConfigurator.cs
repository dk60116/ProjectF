using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class TerrainPhaseOneConfigurator
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string SourceVolumeProfilePath = "Assets/URP/New Volume Profile.asset";
    private const string ArtRootFolder = "Assets/_TerrainArt";
    private const string PhaseOneFolder = ArtRootFolder + "/PhaseOne";
    private const string GrassMeshPath = PhaseOneFolder + "/GrassClump_PhaseOne.mesh";
    private const string GrassMaterialPath = PhaseOneFolder + "/M_GrassClump_PhaseOne.mat";
    private const string VolumeProfilePath = PhaseOneFolder + "/Terrain_PhaseOne_Volume.asset";
    private const string PreviewPath = "Library/TerrainPhaseOnePreview.png";

    [MenuItem("Tools/ProjectF/Terrain/Apply Phase One Art Direction")]
    public static void ApplyPhaseOneArtDirection()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != GameScenePath)
        {
            Debug.LogError($"Terrain Phase One: open {GameScenePath} before applying the preset.");
            return;
        }

        EnsureAssetFolders();
        Mesh grassMesh = CreateOrUpdateGrassMesh();
        Material grassMaterial = CreateOrUpdateGrassMaterial();
        VolumeProfile volumeProfile = CreateOrUpdateVolumeProfile();

        ConfigureDirectionalLight();
        ConfigureEnvironmentLighting();
        ConfigureGlobalVolume(volumeProfile);
        ConfigureTerrainGenerator(grassMesh, grassMaterial);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SceneView.RepaintAll();

        Debug.Log(
            $"Terrain Phase One: applied neutral afternoon lighting, macro ground variation, " +
            $"and sparse combined grass ({grassMesh.vertexCount} source vertices).",
            grassMesh);
    }

    [MenuItem("Tools/ProjectF/Terrain/Capture Phase One Preview")]
    public static void CapturePhaseOnePreview()
    {
        string absolutePath = Path.GetFullPath(PreviewPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        ScreenCapture.CaptureScreenshot(absolutePath);
        Debug.Log($"Terrain Phase One: preview capture requested at {absolutePath}.");
    }

    [MenuItem("Tools/ProjectF/Terrain/Capture Phase One Preview", true)]
    private static bool CanCapturePhaseOnePreview()
    {
        return EditorApplication.isPlaying;
    }

    private static void ConfigureDirectionalLight()
    {
        GameObject lightObject = GameObject.Find("Directional Light");
        Light directionalLight = lightObject != null ? lightObject.GetComponent<Light>() : null;
        if (directionalLight == null || directionalLight.type != LightType.Directional)
        {
            throw new MissingReferenceException("Terrain Phase One: Directional Light was not found.");
        }

        Undo.RecordObjects(new Object[] { lightObject.transform, directionalLight }, "Apply terrain phase one lighting");
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        directionalLight.color = new Color(1f, 0.99f, 0.96f, 1f);
        directionalLight.intensity = 1.10f;
        directionalLight.useColorTemperature = false;
        directionalLight.colorTemperature = 5500f;
        directionalLight.shadows = LightShadows.Soft;
        directionalLight.shadowStrength = 0.88f;
        directionalLight.shadowBias = 0.04f;
        directionalLight.shadowNormalBias = 0.35f;
        directionalLight.bounceIntensity = 0.8f;
        EditorUtility.SetDirty(lightObject.transform);
        EditorUtility.SetDirty(directionalLight);
    }

    private static void ConfigureEnvironmentLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.46f, 0.47f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.28f, 0.31f, 0.32f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.18f, 0.19f, 1f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0.7f;
        RenderSettings.subtractiveShadowColor = new Color(0.20f, 0.23f, 0.25f, 1f);
    }

    private static void ConfigureGlobalVolume(VolumeProfile volumeProfile)
    {
        GameObject volumeObject = GameObject.Find("Global Volume");
        Volume volume = volumeObject != null ? volumeObject.GetComponent<Volume>() : null;
        if (volume == null)
        {
            throw new MissingReferenceException("Terrain Phase One: Global Volume was not found.");
        }

        Undo.RecordObject(volume, "Assign terrain phase one volume");
        volume.isGlobal = true;
        volume.weight = 1f;
        volume.sharedProfile = volumeProfile;
        EditorUtility.SetDirty(volume);
    }

    private static void ConfigureTerrainGenerator(Mesh grassMesh, Material grassMaterial)
    {
        GameObject terrainObject = GameObject.Find("Terrain");
        TerrainGenerator terrainGenerator = terrainObject != null
            ? terrainObject.GetComponent<TerrainGenerator>()
            : null;
        if (terrainGenerator == null)
        {
            throw new MissingReferenceException("Terrain Phase One: TerrainGenerator was not found.");
        }

        SerializedObject serializedTerrain = new SerializedObject(terrainGenerator);
        SetFloat(serializedTerrain, "generatedSurfaceBlendTextureTiling", 0.24f);
        SetFloat(serializedTerrain, "generatedSurfaceBlendNoiseScale", 0.10f);
        SetFloat(serializedTerrain, "generatedSurfaceBlendNoiseStrength", 0.22f);
        SetFloat(serializedTerrain, "generatedSurfaceLargeVariationScale", 0.035f);
        SetFloat(serializedTerrain, "generatedSurfaceLargeVariationStrength", 0.18f);
        SetColor(serializedTerrain, "generatedSurfaceBlendSandTint", new Color(0.94f, 1f, 1f, 1f));
        SetColor(serializedTerrain, "generatedSurfaceBlendDirtTint", new Color(0.82f, 0.93f, 0.96f, 1f));
        SetColor(serializedTerrain, "generatedSurfaceBlendGrassTint", new Color(0.85f, 1.05f, 0.96f, 1f));
        SetColor(serializedTerrain, "generatedSurfaceBlendForestTint", new Color(0.70f, 0.92f, 0.82f, 1f));
        SetColor(serializedTerrain, "generatedSurfaceBlendShadowColor", new Color(0.45f, 0.54f, 0.55f, 1f));
        SetFloat(serializedTerrain, "generatedSurfaceBlendShadeThreshold", 0.48f);
        SetFloat(serializedTerrain, "generatedSurfaceBlendShadeSmoothness", 0.08f);
        SetBool(serializedTerrain, "generateSurfaceGrass", true);
        SetObject(serializedTerrain, "surfaceGrassMesh", grassMesh);
        SetObject(serializedTerrain, "surfaceGrassMaterial", grassMaterial);
        SetFloat(serializedTerrain, "surfaceGrassDensity", 0.16f);
        SetVector2(serializedTerrain, "surfaceGrassScaleRange", new Vector2(0.8f, 1.2f));
        SetFloat(serializedTerrain, "surfaceGrassYOffset", 0.015f);
        serializedTerrain.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(terrainGenerator);
    }

    private static VolumeProfile CreateOrUpdateVolumeProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            if (!AssetDatabase.CopyAsset(SourceVolumeProfilePath, VolumeProfilePath))
            {
                throw new MissingReferenceException("Terrain Phase One: source Volume Profile could not be copied.");
            }

            AssetDatabase.ImportAsset(VolumeProfilePath, ImportAssetOptions.ForceSynchronousImport);
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        }

        ColorAdjustments colorAdjustments = GetOrAdd<ColorAdjustments>(profile);
        colorAdjustments.active = true;
        colorAdjustments.postExposure.Override(0.15f);
        colorAdjustments.contrast.Override(6f);
        colorAdjustments.colorFilter.Override(new Color(0.94f, 0.99f, 1f, 1f));
        colorAdjustments.saturation.Override(0f);

        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.active = true;
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.18f);
        bloom.scatter.Override(0.55f);

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.active = true;
        vignette.color.Override(new Color(0.03f, 0.045f, 0.045f, 1f));
        vignette.intensity.Override(0.10f);
        vignette.smoothness.Override(0.50f);

        Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
        tonemapping.active = true;
        tonemapping.mode.Override(TonemappingMode.ACES);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
        {
            return component;
        }

        return profile.Add<T>(true);
    }

    private static Mesh CreateOrUpdateGrassMesh()
    {
        Mesh generatedMesh = BuildGrassClumpMesh();
        Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GrassMeshPath);
        if (assetMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, GrassMeshPath);
            return generatedMesh;
        }

        EditorUtility.CopySerialized(generatedMesh, assetMesh);
        Object.DestroyImmediate(generatedMesh);
        assetMesh.name = "GrassClump_PhaseOne";
        EditorUtility.SetDirty(assetMesh);
        return assetMesh;
    }

    private static Mesh BuildGrassClumpMesh()
    {
        List<Vector3> vertices = new List<Vector3>(35);
        List<int> triangles = new List<int>(126);
        const int bladeCount = 7;
        for (int bladeIndex = 0; bladeIndex < bladeCount; bladeIndex++)
        {
            float angle = bladeIndex * (360f / bladeCount) + ((bladeIndex % 2) * 11f);
            float height = 0.22f + ((bladeIndex % 3) * 0.035f);
            float width = 0.055f + ((bladeIndex % 2) * 0.015f);
            float lean = 0.055f + ((bladeIndex % 4) * 0.012f);
            AddGrassBlade(vertices, triangles, angle, height, width, lean);
        }

        Mesh mesh = new Mesh { name = "GrassClump_PhaseOne" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddGrassBlade(
        List<Vector3> vertices,
        List<int> triangles,
        float angle,
        float height,
        float width,
        float lean)
    {
        Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Vector3 baseCenter = forward * 0.018f;
        Vector3 side = right * (width * 0.5f);
        Vector3 depth = forward * 0.012f;
        Vector3 tip = baseCenter + (forward * lean) + (Vector3.up * height);
        int start = vertices.Count;

        vertices.Add(baseCenter - side - depth);
        vertices.Add(baseCenter + side - depth);
        vertices.Add(baseCenter + side + depth);
        vertices.Add(baseCenter - side + depth);
        vertices.Add(tip);

        triangles.AddRange(new[]
        {
            start + 0, start + 1, start + 4,
            start + 1, start + 2, start + 4,
            start + 2, start + 3, start + 4,
            start + 3, start + 0, start + 4,
            start + 0, start + 3, start + 2,
            start + 0, start + 2, start + 1
        });
    }

    private static Material CreateOrUpdateGrassMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            throw new MissingReferenceException("Terrain Phase One: a URP Lit shader was not found.");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(GrassMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "M_GrassClump_PhaseOne" };
            AssetDatabase.CreateAsset(material, GrassMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        Color grassColor = new Color(0.42f, 0.66f, 0.20f, 1f);
        SetMaterialColorIfPresent(material, "_BaseColor", grassColor);
        SetMaterialColorIfPresent(material, "_Color", grassColor);
        SetMaterialColorIfPresent(material, "_SpecColor", new Color(0.04f, 0.055f, 0.025f, 1f));
        SetMaterialFloatIfPresent(material, "_Smoothness", 0.05f);
        SetMaterialFloatIfPresent(material, "_ReceiveShadows", 1f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureAssetFolders()
    {
        if (!AssetDatabase.IsValidFolder(ArtRootFolder))
        {
            AssetDatabase.CreateFolder("Assets", "_TerrainArt");
        }

        if (!AssetDatabase.IsValidFolder(PhaseOneFolder))
        {
            AssetDatabase.CreateFolder(ArtRootFolder, "PhaseOne");
        }
    }

    private static void SetFloat(SerializedObject target, string propertyName, float value)
    {
        RequireProperty(target, propertyName).floatValue = value;
    }

    private static void SetBool(SerializedObject target, string propertyName, bool value)
    {
        RequireProperty(target, propertyName).boolValue = value;
    }

    private static void SetColor(SerializedObject target, string propertyName, Color value)
    {
        RequireProperty(target, propertyName).colorValue = value;
    }

    private static void SetVector2(SerializedObject target, string propertyName, Vector2 value)
    {
        RequireProperty(target, propertyName).vector2Value = value;
    }

    private static void SetObject(SerializedObject target, string propertyName, Object value)
    {
        RequireProperty(target, propertyName).objectReferenceValue = value;
    }

    private static SerializedProperty RequireProperty(SerializedObject target, string propertyName)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property == null)
        {
            throw new System.MissingMemberException(target.targetObject.GetType().Name, propertyName);
        }

        return property;
    }

    private static void SetMaterialColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    private static void SetMaterialFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }
}
