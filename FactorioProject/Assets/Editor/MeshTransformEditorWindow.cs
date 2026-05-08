using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

public class MeshTransformEditorWindow : EditorWindow
{
    private const float PresetButtonHeight = 24f;
    private const float PreviewHeight = 260f;
    private const float PivotCenterPickRadius = 10f;
    private const float PivotAxisPickRadius = 7f;
    private const string UniversalPipelineShaderTag = "UniversalPipeline";
    private const string LegacyLightweightPipelineShaderTag = "LightweightPipeline";
    private const string HdPipelineShaderTag = "HDRenderPipeline";
    private static readonly Color MissingMaterialPreviewColor = Color.white;
    private static readonly string[] MissingMaterialPreviewShaderNames =
    {
        "Custom/ToonCharacter",
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Lit",
        "Unlit/Color",
        "Legacy Shaders/Diffuse",
        "Standard",
        "Hidden/Internal-Colored"
    };

    private enum PivotDragMode
    {
        None,
        Plane,
        AxisX,
        AxisY,
        AxisZ
    }

    [SerializeField]
    private Vector2 scrollPosition;
    [SerializeField]
    private GameObject sourceModel;
    [SerializeField]
    private Mesh sourceMesh;
    [SerializeField]
    private bool syncSelection = true;
    [SerializeField]
    private Vector3 positionOffset = Vector3.zero;
    [SerializeField]
    private Vector3 pivotPosition = Vector3.zero;
    [SerializeField]
    private Vector3 rotationEuler = Vector3.zero;
    [SerializeField]
    private Vector3 scale = Vector3.one;
    [SerializeField]
    private bool fixMirroredWinding = true;
    [SerializeField]
    private bool recalculateNormals = true;
    [SerializeField]
    private bool recalculateTangents = true;
    [SerializeField]
    private string outputSuffix = "_Edited";
    [SerializeField]
    private Vector2 previewOrbit = new Vector2(135f, -20f);
    [SerializeField]
    private float previewZoom = 1.35f;
    [SerializeField]
    private bool showPivotInPreview = true;

    private PreviewRenderUtility previewRenderer;
    private Material previewMaterial;
    private Material[] previewMaterials = new Material[0];
    private Mesh previewMesh;
    private Mesh previewMeshSource;
    private GameObject previewModelSource;
    private bool previewDirty = true;
    private PivotDragMode pivotDragMode = PivotDragMode.None;
    private Vector3 pivotDragStartPreviewPosition;
    private Vector3 pivotDragStartPointerPreviewPosition;
    private Vector3 pivotDragAxis = Vector3.right;
    private float pivotDragStartAxisParameter;

    [MenuItem("Window/ProjectF/Mesh Transform")]
    [MenuItem("Tools/MapObject/Mesh Transform")]
    public static void ShowWindow()
    {
        MeshTransformEditorWindow window = GetWindow<MeshTransformEditorWindow>("Mesh Transform");
        window.minSize = new Vector2(420f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        TrySyncWithSelection();
        EnsurePreviewRenderer();
    }

    private void OnDisable()
    {
        DisposePreviewResources();
    }

    private void OnSelectionChange()
    {
        if (!syncSelection)
        {
            return;
        }

        TrySyncWithSelection();
        previewDirty = true;
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3D Mesh Transform", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("메쉬 에셋의 버텍스에 Position / Rotation / Scale을 구워서 새 Mesh Asset으로 저장합니다.", MessageType.Info);
        DrawPreviewSection();
        EditorGUILayout.Space(8f);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUI.BeginChangeCheck();
        DrawSourceSection();
        EditorGUILayout.Space(8f);
        DrawTransformSection();
        EditorGUILayout.Space(8f);
        DrawOptionsSection();
        EditorGUILayout.Space(12f);
        DrawSaveButtons();
        EditorGUILayout.Space(8f);
        if (EditorGUI.EndChangeCheck())
        {
            previewDirty = true;
        }
        EditorGUILayout.EndScrollView();

        if (Event.current.type == EventType.Repaint)
        {
            Repaint();
        }
    }

    private void DrawSourceSection()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        GameObject nextSourceModel = (GameObject)EditorGUILayout.ObjectField("FBX / Model", sourceModel, typeof(GameObject), false);
        if (nextSourceModel != sourceModel)
        {
            SetSourceModel(nextSourceModel);
        }

        using (new EditorGUI.DisabledScope(sourceModel != null))
        {
            Mesh nextSourceMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", sourceMesh, typeof(Mesh), false);
            if (nextSourceMesh != sourceMesh)
            {
                SetSourceMesh(nextSourceMesh);
            }
        }

        syncSelection = EditorGUILayout.ToggleLeft("선택한 Mesh와 자동 동기화", syncSelection);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Selection"))
            {
                TrySyncWithSelection(true);
            }

            if (GUILayout.Button("Reset Transform"))
            {
                positionOffset = Vector3.zero;
                pivotPosition = Vector3.zero;
                rotationEuler = Vector3.zero;
                scale = Vector3.one;
                previewDirty = true;
            }
        }

        if (!HasSource())
        {
            EditorGUILayout.HelpBox("Project 창의 FBX / Mesh Asset 또는 MeshFilter가 있는 오브젝트를 선택하면 자동으로 가져옵니다.", MessageType.Warning);
            return;
        }

        string assetPath = GetActiveSourcePath();
        EditorGUILayout.LabelField("Path", string.IsNullOrWhiteSpace(assetPath) ? "(Scene Mesh)" : assetPath, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Mode", sourceModel != null ? "FBX / Model 전체 메쉬" : "Single Mesh");
        EditorGUILayout.LabelField("Meshes", GetSourceMeshCount().ToString());
        EditorGUILayout.LabelField("Vertices", GetSourceVertexCount().ToString());
        if (TryGetSourceBounds(out Bounds bounds))
        {
            EditorGUILayout.LabelField("Bounds Center", bounds.center.ToString("F3"));
            EditorGUILayout.LabelField("Bounds Size", bounds.size.ToString("F3"));
        }
    }

    private void DrawTransformSection()
    {
        EditorGUILayout.LabelField("Transform", EditorStyles.boldLabel);
        DrawPivotSection();
        EditorGUILayout.Space(6f);
        positionOffset = EditorGUILayout.Vector3Field("Position", positionOffset);
        rotationEuler = EditorGUILayout.Vector3Field("Rotation", rotationEuler);
        scale = EditorGUILayout.Vector3Field("Scale", scale);
    }

    private void DrawPivotSection()
    {
        if (!HasSource())
        {
            return;
        }

        EditorGUILayout.LabelField("Pivot", EditorStyles.miniBoldLabel);
        pivotPosition = EditorGUILayout.Vector3Field("Pivot Position", pivotPosition);
        showPivotInPreview = EditorGUILayout.ToggleLeft("Preview에 Pivot 기즈모 표시 / 드래그 편집", showPivotInPreview);
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Pivot Preset", EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Origin", GUILayout.Height(PresetButtonHeight)))
            {
                pivotPosition = Vector3.zero;
                previewDirty = true;
            }

            if (GUILayout.Button("Center", GUILayout.Height(PresetButtonHeight)))
            {
                if (TryGetSourceBounds(out Bounds bounds))
                {
                    pivotPosition = bounds.center;
                }

                previewDirty = true;
            }

            if (GUILayout.Button("Bottom", GUILayout.Height(PresetButtonHeight)))
            {
                if (TryGetSourceBounds(out Bounds bounds))
                {
                    pivotPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                }

                previewDirty = true;
            }

            if (GUILayout.Button("Top", GUILayout.Height(PresetButtonHeight)))
            {
                if (TryGetSourceBounds(out Bounds bounds))
                {
                    pivotPosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                }

                previewDirty = true;
            }
        }
    }

    private void DrawOptionsSection()
    {
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        outputSuffix = EditorGUILayout.TextField("Output Suffix", outputSuffix);
        fixMirroredWinding = EditorGUILayout.ToggleLeft("음수 Scale일 때 삼각형 뒤집힘 보정", fixMirroredWinding);
        recalculateNormals = EditorGUILayout.ToggleLeft("Normals 다시 계산", recalculateNormals);
        recalculateTangents = EditorGUILayout.ToggleLeft("Tangents 다시 계산", recalculateTangents);
    }

    private void DrawPreviewSection()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("드래그: 회전 / 휠: 줌", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset View", GUILayout.Width(90f)))
            {
                previewOrbit = new Vector2(135f, -20f);
                previewZoom = 1.35f;
                previewDirty = true;
            }
        }

        Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        GUI.Box(previewRect, GUIContent.none);

        if (!HasSource())
        {
            EditorGUI.DropShadowLabel(previewRect, "Preview unavailable");
            return;
        }

        DrawMeshPreview(previewRect);
        HandlePreviewInput(previewRect);
    }

    private void DrawSaveButtons()
    {
        string sourcePath = GetActiveSourcePath();
        bool canSave = CanSaveToSource(sourcePath, out string saveInfoMessage);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(!canSave);
            if (GUILayout.Button("Save", GUILayout.Height(34f)))
            {
                SaveToSourceMesh();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!HasSource());
            if (GUILayout.Button("Save As", GUILayout.Height(34f)))
            {
                CreateTransformedAsset();
            }
            EditorGUI.EndDisabledGroup();
        }

        if (HasSource() && string.IsNullOrWhiteSpace(sourcePath))
        {
            EditorGUILayout.HelpBox("씬 인스턴스 메쉬는 직접 저장할 수 없습니다. Save As를 사용하세요.", MessageType.Info);
            return;
        }

        if (!string.IsNullOrWhiteSpace(saveInfoMessage))
        {
            MessageType messageType = canSave ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(saveInfoMessage, messageType);
        }
    }

    private void TrySyncWithSelection(bool force = false)
    {
        if (!force && !syncSelection)
        {
            return;
        }

        GameObject selectedModel = ResolveSelectedModel();
        if (selectedModel != null)
        {
            SetSourceModel(selectedModel);
            return;
        }

        Mesh selectedMesh = ResolveSelectedMesh();
        if (selectedMesh != null)
        {
            SetSourceMesh(selectedMesh);
        }
    }

    private static GameObject ResolveSelectedModel()
    {
        if (Selection.activeObject is not GameObject selectedObject)
        {
            return null;
        }

        string assetPath = AssetDatabase.GetAssetPath(selectedObject);
        GameObject modelRoot = LoadModelRootForAssetPath(assetPath);
        return selectedObject == modelRoot ? selectedObject : null;
    }

    private static Mesh ResolveSelectedMesh()
    {
        if (Selection.activeObject is Mesh mesh)
        {
            return mesh;
        }

        if (Selection.activeGameObject == null)
        {
            return null;
        }

        MeshFilter meshFilter = Selection.activeGameObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = Selection.activeGameObject.GetComponentInChildren<MeshFilter>(true);
        }

        return meshFilter != null ? meshFilter.sharedMesh : null;
    }

    private void SetSourceModel(GameObject model)
    {
        if (model != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(model);
            GameObject modelRoot = LoadModelRootForAssetPath(assetPath);
            if (modelRoot == null || model != modelRoot)
            {
                SetSourceMesh(FindFirstMeshInModel(model));
                return;
            }
        }

        if (sourceModel == model)
        {
            return;
        }

        sourceModel = model;
        sourceMesh = model != null ? FindFirstMeshInModel(model) : null;
        previewDirty = true;
    }

    private void SetSourceMesh(Mesh mesh)
    {
        if (sourceMesh == mesh && sourceModel == null)
        {
            return;
        }

        sourceModel = null;
        sourceMesh = mesh;
        previewDirty = true;
    }

    private bool HasSource()
    {
        return sourceModel != null || sourceMesh != null;
    }

    private string GetActiveSourcePath()
    {
        if (sourceModel != null)
        {
            return AssetDatabase.GetAssetPath(sourceModel);
        }

        return sourceMesh != null ? AssetDatabase.GetAssetPath(sourceMesh) : string.Empty;
    }

    private int GetSourceMeshCount()
    {
        if (sourceModel != null)
        {
            MeshFilter[] meshFilters = sourceModel.GetComponentsInChildren<MeshFilter>(true);
            int count = 0;
            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (meshFilters[i] != null && meshFilters[i].sharedMesh != null)
                {
                    count++;
                }
            }

            return count;
        }

        return sourceMesh != null ? 1 : 0;
    }

    private int GetSourceVertexCount()
    {
        if (sourceModel != null)
        {
            MeshFilter[] meshFilters = sourceModel.GetComponentsInChildren<MeshFilter>(true);
            int count = 0;
            for (int i = 0; i < meshFilters.Length; i++)
            {
                Mesh mesh = meshFilters[i] != null ? meshFilters[i].sharedMesh : null;
                if (mesh != null)
                {
                    count += mesh.vertexCount;
                }
            }

            return count;
        }

        return sourceMesh != null ? sourceMesh.vertexCount : 0;
    }

    private bool TryGetSourceBounds(out Bounds bounds)
    {
        if (sourceModel != null)
        {
            return TryCalculateModelBounds(sourceModel, out bounds);
        }

        if (sourceMesh != null)
        {
            bounds = sourceMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryCalculateModelBounds(GameObject modelRoot, out Bounds bounds)
    {
        bounds = default;
        if (modelRoot == null)
        {
            return false;
        }

        bool hasBounds = false;
        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                continue;
            }

            Matrix4x4 meshToRoot = GetTransformToRoot(modelRoot.transform, meshFilter.transform);
            EncapsulateTransformedBounds(mesh.bounds, meshToRoot, ref bounds, ref hasBounds);
        }

        return hasBounds;
    }

    private static void EncapsulateTransformedBounds(
        Bounds sourceBounds,
        Matrix4x4 matrix,
        ref Bounds bounds,
        ref bool hasBounds)
    {
        Vector3 min = sourceBounds.min;
        Vector3 max = sourceBounds.max;
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 transformedCorner = matrix.MultiplyPoint3x4(corner);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(transformedCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(transformedCorner);
                    }
                }
            }
        }
    }

    private static Matrix4x4 GetTransformToRoot(Transform root, Transform target)
    {
        return root.worldToLocalMatrix * target.localToWorldMatrix;
    }

    private static Mesh FindFirstMeshInModel(GameObject modelRoot)
    {
        if (modelRoot == null)
        {
            return null;
        }

        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            Mesh mesh = meshFilters[i] != null ? meshFilters[i].sharedMesh : null;
            if (mesh != null)
            {
                return mesh;
            }
        }

        return null;
    }

    private static bool IsModelAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string extension = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? string.Empty;
        return extension == ".fbx";
    }

    private static GameObject LoadModelRootForAssetPath(string assetPath)
    {
        return IsModelAssetPath(assetPath)
            ? AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
            : null;
    }

    private static bool IsSameMeshAsset(Mesh mesh, Mesh targetMesh)
    {
        if (mesh == null || targetMesh == null)
        {
            return false;
        }

        if (mesh == targetMesh)
        {
            return true;
        }

        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string meshGuid, out long meshLocalId)
            && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(targetMesh, out string targetGuid, out long targetLocalId))
        {
            return meshGuid == targetGuid && meshLocalId == targetLocalId;
        }

        return AssetDatabase.GetAssetPath(mesh) == AssetDatabase.GetAssetPath(targetMesh)
            && mesh.name == targetMesh.name;
    }

    private void CreateTransformedAsset()
    {
        if (sourceModel != null)
        {
            CreateTransformedModelFbxAsset();
            return;
        }

        CreateTransformedMeshAsset();
    }

    private void CreateTransformedMeshAsset()
    {
        if (sourceMesh == null)
        {
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(sourceMesh);
        string defaultDirectory = string.IsNullOrWhiteSpace(sourcePath)
            ? "Assets"
            : Path.GetDirectoryName(sourcePath)?.Replace("\\", "/") ?? "Assets";
        string defaultName = $"{sourceMesh.name}{outputSuffix}";

        string outputPath = EditorUtility.SaveFilePanelInProject(
            "Create Transformed Mesh",
            defaultName,
            "asset",
            "저장할 Mesh Asset 경로를 선택하세요.",
            defaultDirectory);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        Mesh newMesh = Instantiate(sourceMesh);
        newMesh.name = Path.GetFileNameWithoutExtension(outputPath);

        ApplyTransform(newMesh);

        AssetDatabase.CreateAsset(newMesh, outputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(newMesh);
        Selection.activeObject = newMesh;
        previewDirty = true;
    }

    private void CreateTransformedModelFbxAsset()
    {
        if (sourceModel == null)
        {
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(sourceModel);
        if (!CanOverwriteFbxSource(sourcePath, true, out string reason))
        {
            EditorUtility.DisplayDialog("Mesh Transform", reason, "OK");
            return;
        }

        string defaultDirectory = string.IsNullOrWhiteSpace(sourcePath)
            ? "Assets"
            : Path.GetDirectoryName(sourcePath)?.Replace("\\", "/") ?? "Assets";
        string defaultName = $"{sourceModel.name}{outputSuffix}";

        string outputPath = EditorUtility.SaveFilePanelInProject(
            "Create Transformed FBX",
            defaultName,
            "fbx",
            "저장할 FBX Asset 경로를 선택하세요.",
            defaultDirectory);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        if (ExportTransformedModelToFbx(outputPath, sourceModel))
        {
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            UnityEngine.Object exportedObject = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
            if (exportedObject != null)
            {
                EditorGUIUtility.PingObject(exportedObject);
                Selection.activeObject = exportedObject;
            }

            previewDirty = true;
        }
    }

    private void SaveToSourceMesh()
    {
        if (!HasSource())
        {
            return;
        }

        string sourcePath = GetActiveSourcePath();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            EditorUtility.DisplayDialog("Mesh Transform", "원본 Mesh Asset 경로를 찾을 수 없습니다. Save As를 사용하세요.", "OK");
            return;
        }

        string extension = Path.GetExtension(sourcePath)?.ToLowerInvariant() ?? string.Empty;
        switch (extension)
        {
            case ".obj":
                OverwriteObjSource(sourcePath);
                return;

            case ".fbx":
                ExportBackToFbxSource(sourcePath);
                return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Mesh Transform",
            $"선택한 메쉬를 직접 수정합니다.\n\n{sourcePath}",
            "Save",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        Undo.RegisterCompleteObjectUndo(sourceMesh, "Save Mesh Transform");
        ApplyTransform(sourceMesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(sourceMesh);
        previewDirty = true;
    }

    private bool CanSaveToSource(string sourcePath, out string message)
    {
        message = string.Empty;

        if (!HasSource() || string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        string extension = Path.GetExtension(sourcePath)?.ToLowerInvariant() ?? string.Empty;
        switch (extension)
        {
            case ".obj":
                message = "Save를 누르면 원본 OBJ 파일을 직접 덮어써서, 다른 프로그램에서 열어도 같은 메쉬가 보입니다.";
                return true;

            case ".fbx":
                if (CanOverwriteFbxSource(sourcePath, true, out string fbxReason))
                {
                    message = sourceModel != null
                        ? "Save를 누르면 FBX 안의 모든 MeshFilter를 변환해서 원본 FBX 파일을 직접 덮어씁니다."
                        : "Save를 누르면 FBX 안에서 선택한 Mesh만 변환해서 원본 FBX 파일을 직접 덮어씁니다.";
                    return true;
                }

                message = fbxReason;
                return false;

            default:
                message = "Save를 누르면 현재 Mesh Asset 데이터를 저장합니다.";
                return true;
        }
    }

    private bool CanOverwriteFbxSource(string sourcePath, bool allowMultipleMeshes, out string reason)
    {
        reason = string.Empty;

        GameObject modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (modelRoot == null)
        {
            reason = "이 FBX는 모델 루트를 찾을 수 없어 원본 덮어쓰기를 지원하지 않습니다.";
            return false;
        }

        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters == null || meshFilters.Length == 0)
        {
            reason = "메쉬가 없는 FBX는 원본 덮어쓰기를 지원하지 않습니다.";
            return false;
        }

        if (!allowMultipleMeshes && meshFilters.Length != 1)
        {
            reason = "여러 메쉬가 들어있는 FBX는 원본 덮어쓰기를 막았습니다. Save As를 사용하세요.";
            return false;
        }

        if (modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
        {
            reason = "SkinnedMeshRenderer가 있는 FBX는 리그 손상 위험 때문에 원본 덮어쓰기를 막았습니다.";
            return false;
        }

        int meshAssetCount = 0;
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh)
            {
                meshAssetCount++;
            }
        }

        if (!allowMultipleMeshes && meshAssetCount != 1)
        {
            reason = "여러 Mesh sub-asset이 들어있는 FBX는 원본 덮어쓰기를 막았습니다.";
            return false;
        }

        return true;
    }

    private void OverwriteObjSource(string sourcePath)
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Mesh Transform",
            $"원본 OBJ 파일을 직접 덮어씁니다.\n\n{sourcePath}",
            "Save",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        Mesh transformedMesh = CreateTransformedMeshCopy();
        if (transformedMesh == null)
        {
            return;
        }

        try
        {
            string absolutePath = GetAbsoluteProjectPath(sourcePath);
            File.WriteAllText(absolutePath, BuildObjText(transformedMesh), Encoding.UTF8);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            previewDirty = true;
        }
        finally
        {
            DestroyImmediate(transformedMesh);
        }
    }

    private void ExportBackToFbxSource(string sourcePath)
    {
        GameObject modelRoot = sourceModel != null ? sourceModel : LoadModelRootForAssetPath(sourcePath);
        if (!CanOverwriteFbxSource(sourcePath, true, out string reason))
        {
            EditorUtility.DisplayDialog("Mesh Transform", reason, "OK");
            return;
        }

        if (modelRoot != null)
        {
            Mesh targetMesh = sourceModel != null ? null : sourceMesh;
            string scopeDescription = targetMesh == null
                ? "원본 FBX 안의 모든 MeshFilter"
                : $"원본 FBX 안의 선택한 Mesh만\n\nMesh: {targetMesh.name}";
            bool modelExportConfirmed = EditorUtility.DisplayDialog(
                "Mesh Transform",
                $"{scopeDescription} 변환해서 원본 파일을 직접 덮어씁니다.\nSkinnedMeshRenderer는 지원하지 않으며, 애니메이션 클립은 보존되지 않을 수 있습니다.\n\n{sourcePath}",
                "Export",
                "Cancel");

            if (!modelExportConfirmed)
            {
                return;
            }

            if (ExportTransformedModelToFbx(sourcePath, modelRoot, targetMesh))
            {
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                previewDirty = true;
            }

            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Mesh Transform",
            $"원본 FBX 파일을 정적 메쉬로 다시 내보냅니다.\n리그/애니메이션/복수 메쉬 FBX는 지원하지 않습니다.\n\n{sourcePath}",
            "Export",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        Mesh transformedMesh = CreateTransformedMeshCopy();
        if (transformedMesh == null)
        {
            return;
        }

        GameObject tempRoot = null;
        try
        {
            tempRoot = new GameObject("MeshTransformExportRoot");
            tempRoot.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter = tempRoot.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = transformedMesh;

            MeshRenderer meshRenderer = tempRoot.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = BuildExportMaterials(transformedMesh.subMeshCount);

            string absolutePath = GetAbsoluteProjectPath(sourcePath);
            string exportedPath = ModelExporter.ExportObject(absolutePath, tempRoot);
            if (string.IsNullOrWhiteSpace(exportedPath))
            {
                EditorUtility.DisplayDialog("Mesh Transform", "FBX 내보내기에 실패했습니다.", "OK");
                return;
            }

            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            previewDirty = true;
        }
        finally
        {
            if (tempRoot != null)
            {
                DestroyImmediate(tempRoot);
            }

            DestroyImmediate(transformedMesh);
        }
    }

    private bool ExportTransformedModelToFbx(string assetPath, GameObject modelRoot, Mesh targetMesh = null)
    {
        if (modelRoot == null)
        {
            return false;
        }

        GameObject tempRoot = null;
        List<Mesh> temporaryMeshes = new List<Mesh>();
        try
        {
            tempRoot = Instantiate(modelRoot);
            tempRoot.name = Path.GetFileNameWithoutExtension(assetPath);
            tempRoot.hideFlags = HideFlags.HideAndDontSave;
            tempRoot.transform.position = Vector3.zero;
            tempRoot.transform.rotation = Quaternion.identity;
            tempRoot.transform.localScale = Vector3.one;

            MeshFilter[] meshFilters = tempRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                Mesh originalMesh = meshFilter != null ? meshFilter.sharedMesh : null;
                if (originalMesh == null)
                {
                    continue;
                }

                if (targetMesh != null && !IsSameMeshAsset(originalMesh, targetMesh))
                {
                    continue;
                }

                Mesh transformedMesh = Instantiate(originalMesh);
                transformedMesh.name = originalMesh.name;
                transformedMesh.hideFlags = HideFlags.HideAndDontSave;
                Matrix4x4 meshToRoot = GetTransformToRoot(tempRoot.transform, meshFilter.transform);
                ApplyTransformInRootSpace(transformedMesh, meshToRoot, false, true);
                meshFilter.sharedMesh = transformedMesh;
                temporaryMeshes.Add(transformedMesh);
            }

            if (temporaryMeshes.Count == 0)
            {
                string message = targetMesh == null
                    ? "내보낼 MeshFilter를 찾을 수 없습니다."
                    : "선택한 Mesh와 일치하는 MeshFilter를 FBX 안에서 찾을 수 없습니다.";
                EditorUtility.DisplayDialog("Mesh Transform", message, "OK");
                return false;
            }

            string absolutePath = GetAbsoluteProjectPath(assetPath);
            string exportedPath = ModelExporter.ExportObject(absolutePath, tempRoot);
            if (string.IsNullOrWhiteSpace(exportedPath))
            {
                EditorUtility.DisplayDialog("Mesh Transform", "FBX 내보내기에 실패했습니다.", "OK");
                return false;
            }

            return true;
        }
        finally
        {
            if (tempRoot != null)
            {
                DestroyImmediate(tempRoot);
            }

            for (int i = 0; i < temporaryMeshes.Count; i++)
            {
                if (temporaryMeshes[i] != null)
                {
                    DestroyImmediate(temporaryMeshes[i]);
                }
            }
        }
    }

    private Mesh CreateTransformedMeshCopy()
    {
        if (sourceMesh == null)
        {
            return null;
        }

        Mesh transformedMesh = Instantiate(sourceMesh);
        transformedMesh.name = sourceMesh.name;
        transformedMesh.hideFlags = HideFlags.HideAndDontSave;
        ApplyTransform(transformedMesh, false);
        return transformedMesh;
    }

    private Material[] BuildExportMaterials(int subMeshCount)
    {
        int materialCount = Mathf.Max(1, subMeshCount);
        Material[] materials = new Material[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            materials[i] = previewMaterial;
        }

        return materials;
    }

    private static string GetAbsoluteProjectPath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static string BuildObjText(Mesh mesh)
    {
        StringBuilder builder = new StringBuilder(mesh.vertexCount * 64);
        builder.AppendLine("# Exported by MeshTransformEditorWindow");

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            builder.Append("v ")
                .Append(vertex.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(vertex.y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(vertex.z.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        Vector2[] uv = mesh.uv;
        bool hasUv = uv != null && uv.Length == vertices.Length;
        if (hasUv)
        {
            for (int i = 0; i < uv.Length; i++)
            {
                Vector2 uvCoord = uv[i];
                builder.Append("vt ")
                    .Append(uvCoord.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(uvCoord.y.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }
        }

        Vector3[] normals = mesh.normals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        if (hasNormals)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 normal = normals[i];
                builder.Append("vn ")
                    .Append(normal.x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(normal.y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(normal.z.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }
        }

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            builder.Append("g submesh_").Append(subMeshIndex).AppendLine();

            int[] triangles = mesh.GetTriangles(subMeshIndex);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                builder.Append("f ");
                AppendObjFaceVertex(builder, triangles[i] + 1, hasUv, hasNormals);
                builder.Append(' ');
                AppendObjFaceVertex(builder, triangles[i + 1] + 1, hasUv, hasNormals);
                builder.Append(' ');
                AppendObjFaceVertex(builder, triangles[i + 2] + 1, hasUv, hasNormals);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void AppendObjFaceVertex(StringBuilder builder, int index, bool hasUv, bool hasNormals)
    {
        builder.Append(index);

        if (hasUv || hasNormals)
        {
            builder.Append('/');
            if (hasUv)
            {
                builder.Append(index);
            }

            if (hasNormals)
            {
                builder.Append('/');
                builder.Append(index);
            }
        }
    }

    private void ApplyTransform(Mesh mesh, bool markDirty = true, bool bakePivotToOrigin = true)
    {
        Quaternion rotation = Quaternion.Euler(rotationEuler);
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 transformedVertex = vertices[i] - pivotPosition;
            transformedVertex = Vector3.Scale(transformedVertex, scale);
            transformedVertex = rotation * transformedVertex;
            if (!bakePivotToOrigin)
            {
                transformedVertex += pivotPosition;
            }

            transformedVertex += positionOffset;
            vertices[i] = transformedVertex;
        }

        mesh.vertices = vertices;

        bool isMirroredScale = scale.x * scale.y * scale.z < 0f;
        if (isMirroredScale && fixMirroredWinding)
        {
            ReverseTriangles(mesh);
        }

        if (!recalculateNormals && normals != null && normals.Length == vertices.Length)
        {
            Vector3 inverseScale = new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 0f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 0f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 0f : 1f / scale.z);

            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 transformedNormal = Vector3.Scale(normals[i], inverseScale);
                transformedNormal = rotation * transformedNormal;
                normals[i] = transformedNormal.normalized;
            }

            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }

        if (!recalculateTangents && tangents != null && tangents.Length == vertices.Length)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 tangentDirection = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                tangentDirection = rotation * Vector3.Scale(tangentDirection, scale);
                tangentDirection.Normalize();
                tangents[i] = new Vector4(
                    tangentDirection.x,
                    tangentDirection.y,
                    tangentDirection.z,
                    isMirroredScale ? -tangents[i].w : tangents[i].w);
            }

            mesh.tangents = tangents;
        }
        else
        {
            mesh.RecalculateTangents();
        }

        mesh.RecalculateBounds();
        if (markDirty)
        {
            EditorUtility.SetDirty(mesh);
        }
    }

    private void ApplyTransformInRootSpace(Mesh mesh, Matrix4x4 meshToRoot, bool markDirty = true, bool bakePivotToOrigin = true)
    {
        Quaternion rotation = Quaternion.Euler(rotationEuler);
        Matrix4x4 rootToMesh = meshToRoot.inverse;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 rootVertex = meshToRoot.MultiplyPoint3x4(vertices[i]);
            Vector3 transformedVertex = rootVertex - pivotPosition;
            transformedVertex = Vector3.Scale(transformedVertex, scale);
            transformedVertex = rotation * transformedVertex;
            if (!bakePivotToOrigin)
            {
                transformedVertex += pivotPosition;
            }

            transformedVertex += positionOffset;
            vertices[i] = rootToMesh.MultiplyPoint3x4(transformedVertex);
        }

        mesh.vertices = vertices;

        bool isMirroredScale = scale.x * scale.y * scale.z < 0f;
        if (isMirroredScale && fixMirroredWinding)
        {
            ReverseTriangles(mesh);
        }

        if (!recalculateNormals && normals != null && normals.Length == vertices.Length)
        {
            Vector3 inverseScale = new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 0f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 0f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 0f : 1f / scale.z);

            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 rootNormal = meshToRoot.MultiplyVector(normals[i]).normalized;
                rootNormal = Vector3.Scale(rootNormal, inverseScale);
                rootNormal = rotation * rootNormal;
                normals[i] = rootToMesh.MultiplyVector(rootNormal).normalized;
            }

            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }

        if (!recalculateTangents && tangents != null && tangents.Length == vertices.Length)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 tangentDirection = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                Vector3 rootTangent = meshToRoot.MultiplyVector(tangentDirection);
                rootTangent = rotation * Vector3.Scale(rootTangent, scale);
                Vector3 localTangent = rootToMesh.MultiplyVector(rootTangent).normalized;
                tangents[i] = new Vector4(
                    localTangent.x,
                    localTangent.y,
                    localTangent.z,
                    isMirroredScale ? -tangents[i].w : tangents[i].w);
            }

            mesh.tangents = tangents;
        }
        else
        {
            mesh.RecalculateTangents();
        }

        mesh.RecalculateBounds();
        if (markDirty)
        {
            EditorUtility.SetDirty(mesh);
        }
    }

    private static void ReverseTriangles(Mesh mesh)
    {
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            int[] triangles = mesh.GetTriangles(subMeshIndex);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
            }

            mesh.SetTriangles(triangles, subMeshIndex);
        }
    }

    private void EnsurePreviewRenderer()
    {
        if (previewRenderer != null)
        {
            return;
        }

        previewRenderer = new PreviewRenderUtility();
        previewRenderer.cameraFieldOfView = 30f;
        previewRenderer.camera.clearFlags = CameraClearFlags.Color;
        previewRenderer.camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        previewRenderer.lights[0].intensity = 1.15f;
        previewRenderer.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
        previewRenderer.lights[1].intensity = 0.9f;
        previewRenderer.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
        previewRenderer.ambientColor = new Color(0.42f, 0.42f, 0.42f, 1f);

        Shader previewShader = FindMissingMaterialPreviewShader();
        if (previewShader == null)
        {
            return;
        }

        previewMaterial = new Material(previewShader)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        ApplyMaterialColor(previewMaterial, MissingMaterialPreviewColor);
    }

    private static Shader FindMissingMaterialPreviewShader()
    {
        for (int i = 0; i < MissingMaterialPreviewShaderNames.Length; i++)
        {
            Shader shader = Shader.Find(MissingMaterialPreviewShaderNames[i]);
            if (shader != null && shader.isSupported)
            {
                return shader;
            }
        }

        return null;
    }

    private static void ApplyMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private void DisposePreviewResources()
    {
        if (previewMesh != null)
        {
            DestroyImmediate(previewMesh);
            previewMesh = null;
        }

        previewMeshSource = null;
        previewModelSource = null;
        previewMaterials = new Material[0];

        if (previewMaterial != null)
        {
            DestroyImmediate(previewMaterial);
            previewMaterial = null;
        }

        if (previewRenderer != null)
        {
            previewRenderer.Cleanup();
            previewRenderer = null;
        }
    }

    private void HandlePreviewInput(Rect previewRect)
    {
        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.Used || GUIUtility.hotControl != 0)
        {
            return;
        }

        if (!previewRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        switch (currentEvent.type)
        {
            case EventType.ScrollWheel:
                previewZoom = Mathf.Clamp(previewZoom + currentEvent.delta.y * 0.03f, 0.35f, 4.5f);
                previewDirty = true;
                currentEvent.Use();
                break;

            case EventType.MouseDrag:
                if (currentEvent.button != 0 && currentEvent.button != 1)
                {
                    break;
                }

                previewOrbit.x += currentEvent.delta.x;
                previewOrbit.y = Mathf.Clamp(previewOrbit.y - currentEvent.delta.y, -89f, 89f);
                previewDirty = true;
                currentEvent.Use();
                break;
        }
    }

    private void DrawMeshPreview(Rect previewRect)
    {
        EnsurePreviewRenderer();
        UpdatePreviewMeshIfNeeded();

        if (previewRenderer == null || previewMaterial == null || previewMesh == null)
        {
            EditorGUI.DropShadowLabel(previewRect, "Preview unavailable");
            return;
        }

        Bounds bounds = GetPreviewBounds();
        Vector3 center = bounds.center;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
        float distance = radius * Mathf.Max(1.8f, previewZoom * 2.4f);

        Quaternion orbitRotation = Quaternion.Euler(previewOrbit.y, previewOrbit.x, 0f);
        Vector3 cameraOffset = orbitRotation * (Vector3.back * distance);

        previewRenderer.camera.transform.position = center + cameraOffset;
        previewRenderer.camera.transform.LookAt(center);
        previewRenderer.camera.nearClipPlane = 0.01f;
        previewRenderer.camera.farClipPlane = Mathf.Max(100f, distance + radius * 6f);

        if (previewMesh.subMeshCount <= 0)
        {
            EditorGUI.DropShadowLabel(previewRect, "Preview unavailable");
            return;
        }

        if (Event.current.type == EventType.Repaint)
        {
            previewRenderer.BeginPreview(previewRect, GUIStyle.none);
            previewRenderer.camera.transform.position = center + cameraOffset;
            previewRenderer.camera.transform.LookAt(center);
            previewRenderer.camera.nearClipPlane = 0.01f;
            previewRenderer.camera.farClipPlane = Mathf.Max(100f, distance + radius * 6f);

            int renderPassCount = Mathf.Max(previewMesh.subMeshCount, previewMaterials != null ? previewMaterials.Length : 0);
            for (int passIndex = 0; passIndex < renderPassCount; passIndex++)
            {
                int subMeshIndex = Mathf.Min(passIndex, previewMesh.subMeshCount - 1);
                Material material = ResolvePreviewMaterial(passIndex);
                previewRenderer.DrawMesh(previewMesh, Matrix4x4.identity, material, subMeshIndex);
            }

            previewRenderer.Render(true, true);
            Texture previewTexture = previewRenderer.EndPreview();
            GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, false);
        }

        DrawPivotGizmo(previewRect, previewRenderer.camera);
    }

    private void UpdatePreviewMeshIfNeeded()
    {
        if (!previewDirty && previewMesh != null && previewMeshSource == sourceMesh && previewModelSource == sourceModel)
        {
            return;
        }

        if (previewMesh != null)
        {
            DestroyImmediate(previewMesh);
            previewMesh = null;
        }

        previewMeshSource = sourceMesh;
        previewModelSource = sourceModel;
        if (sourceModel != null)
        {
            previewMesh = CreateCombinedModelPreviewMesh(sourceModel, out previewMaterials);
            if (previewMesh != null)
            {
                ApplyTransform(previewMesh, false, false);
            }

            previewDirty = false;
            return;
        }

        if (sourceMesh == null)
        {
            previewMaterials = new Material[0];
            previewDirty = false;
            return;
        }

        previewMesh = Instantiate(sourceMesh);
        previewMesh.name = $"{sourceMesh.name}_Preview";
        previewMesh.hideFlags = HideFlags.HideAndDontSave;
        previewMaterials = GetPreviewMaterialsForSourceMesh(sourceMesh);
        ApplyTransform(previewMesh, false, false);
        previewDirty = false;
    }

    private Material ResolvePreviewMaterial(int passIndex)
    {
        if (previewMaterials != null && previewMaterials.Length > 0)
        {
            int materialIndex = Mathf.Min(passIndex, previewMaterials.Length - 1);
            if (IsUsablePreviewMaterial(previewMaterials[materialIndex]))
            {
                return previewMaterials[materialIndex];
            }
        }

        return previewMaterial;
    }

    private static bool IsUsablePreviewMaterial(Material material)
    {
        if (material == null)
        {
            return false;
        }

        return IsUsablePreviewShader(material.shader, material.GetTag("RenderPipeline", false, string.Empty));
    }

    private static bool IsUsablePreviewShader(Shader shader, string shaderPipelineTag)
    {
        if (shader == null || !shader.isSupported)
        {
            return false;
        }

        string shaderName = shader.name;
        if (string.IsNullOrWhiteSpace(shaderName) || shaderName.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return IsShaderCompatibleWithActiveRenderPipeline(shader, shaderPipelineTag);
    }

    private static bool IsShaderCompatibleWithActiveRenderPipeline(Shader shader, string shaderPipelineTag)
    {
        UnityEngine.Rendering.RenderPipelineAsset renderPipeline = GetConfiguredRenderPipeline();
        if (renderPipeline == null)
        {
            return string.IsNullOrWhiteSpace(shaderPipelineTag);
        }

        string renderPipelineTypeName = renderPipeline.GetType().FullName ?? string.Empty;
        string shaderName = shader.name ?? string.Empty;
        if (renderPipelineTypeName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return IsUniversalRenderPipelineShader(shaderPipelineTag, shaderName);
        }

        if (renderPipelineTypeName.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0
            || renderPipelineTypeName.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return string.Equals(shaderPipelineTag, HdPipelineShaderTag, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(shaderPipelineTag);
    }

    private static UnityEngine.Rendering.RenderPipelineAsset GetConfiguredRenderPipeline()
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        }

        if (UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset != null)
        {
            return UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset;
        }

        return QualitySettings.renderPipeline;
    }

    private static bool IsUniversalRenderPipelineShader(string shaderPipelineTag, string shaderName)
    {
        if (string.Equals(shaderPipelineTag, UniversalPipelineShaderTag, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shaderPipelineTag, LegacyLightweightPipelineShaderTag, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(shaderName)
            && shaderName.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase);
    }

    private static Mesh CreateCombinedModelPreviewMesh(GameObject modelRoot, out Material[] materials)
    {
        materials = new Material[0];
        if (modelRoot == null)
        {
            return null;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<List<int>> trianglesBySubMesh = new List<List<int>>();
        List<Material> materialList = new List<Material>();
        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int filterIndex = 0; filterIndex < meshFilters.Length; filterIndex++)
        {
            MeshFilter meshFilter = meshFilters[filterIndex];
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null || mesh.subMeshCount <= 0)
            {
                continue;
            }

            Matrix4x4 meshToRoot = GetTransformToRoot(modelRoot.transform, meshFilter.transform);
            Vector3[] meshVertices = mesh.vertices;
            Vector2[] meshUv = mesh.uv;
            bool hasUv = meshUv != null && meshUv.Length == meshVertices.Length;
            int vertexOffset = vertices.Count;
            for (int vertexIndex = 0; vertexIndex < meshVertices.Length; vertexIndex++)
            {
                vertices.Add(meshToRoot.MultiplyPoint3x4(meshVertices[vertexIndex]));
                uv.Add(hasUv ? meshUv[vertexIndex] : Vector2.zero);
            }

            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            Material[] renderMaterials = BuildRenderPassMaterials(
                mesh,
                meshRenderer != null ? meshRenderer.sharedMaterials : null);
            int renderPassCount = Mathf.Max(mesh.subMeshCount, renderMaterials.Length);
            for (int passIndex = 0; passIndex < renderPassCount; passIndex++)
            {
                int subMeshIndex = Mathf.Min(passIndex, mesh.subMeshCount - 1);
                int[] subMeshTriangles = mesh.GetTriangles(subMeshIndex);
                if (subMeshTriangles == null || subMeshTriangles.Length == 0)
                {
                    continue;
                }

                List<int> triangles = new List<int>(subMeshTriangles.Length);
                for (int triangleIndex = 0; triangleIndex < subMeshTriangles.Length; triangleIndex++)
                {
                    triangles.Add(vertexOffset + subMeshTriangles[triangleIndex]);
                }

                trianglesBySubMesh.Add(triangles);
                materialList.Add(renderMaterials.Length > 0 ? renderMaterials[Mathf.Min(passIndex, renderMaterials.Length - 1)] : null);
            }
        }

        if (vertices.Count == 0 || trianglesBySubMesh.Count == 0)
        {
            return null;
        }

        Mesh combinedMesh = new Mesh
        {
            name = $"{modelRoot.name}_CombinedPreview",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (vertices.Count > 65535)
        {
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        combinedMesh.SetVertices(vertices);
        combinedMesh.SetUVs(0, uv);
        combinedMesh.subMeshCount = trianglesBySubMesh.Count;
        for (int subMeshIndex = 0; subMeshIndex < trianglesBySubMesh.Count; subMeshIndex++)
        {
            combinedMesh.SetTriangles(trianglesBySubMesh[subMeshIndex], subMeshIndex);
        }

        combinedMesh.RecalculateBounds();
        combinedMesh.RecalculateNormals();
        materials = materialList.ToArray();
        return combinedMesh;
    }

    private static Material[] GetPreviewMaterialsForSourceMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return new Material[0];
        }

        string assetPath = AssetDatabase.GetAssetPath(mesh);
        GameObject modelRoot = LoadModelRootForAssetPath(assetPath);
        if (modelRoot != null)
        {
            MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                Mesh candidateMesh = meshFilter != null ? meshFilter.sharedMesh : null;
                if (!IsSameMeshAsset(candidateMesh, mesh))
                {
                    continue;
                }

                MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
                return BuildRenderPassMaterials(mesh, meshRenderer != null ? meshRenderer.sharedMaterials : null);
            }
        }

        return BuildRenderPassMaterials(mesh, null);
    }

    private static Material[] BuildRenderPassMaterials(Mesh mesh, Material[] sharedMaterials)
    {
        int subMeshCount = mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 0;
        int materialCount = sharedMaterials != null ? sharedMaterials.Length : 0;
        int renderPassCount = Mathf.Max(1, Mathf.Max(subMeshCount, materialCount));
        Material[] materials = new Material[renderPassCount];
        for (int passIndex = 0; passIndex < renderPassCount; passIndex++)
        {
            if (materialCount > 0)
            {
                materials[passIndex] = sharedMaterials[Mathf.Min(passIndex, materialCount - 1)];
            }
        }

        return materials;
    }

    private Bounds GetPreviewBounds()
    {
        Bounds bounds = previewMesh.bounds;
        if (showPivotInPreview)
        {
            bounds.Encapsulate(GetPivotPreviewPosition());
        }

        return bounds;
    }

    private Vector3 GetPivotPreviewPosition()
    {
        return pivotPosition + positionOffset;
    }

    private void DrawPivotGizmo(Rect previewRect, Camera previewCamera)
    {
        if (!showPivotInPreview || previewCamera == null)
        {
            return;
        }

        Vector3 pivotPreviewPosition = GetPivotPreviewPosition();
        float gizmoWorldSize = GetPivotGizmoWorldSize(previewCamera, pivotPreviewPosition);
        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        HandlePivotGizmoInput(previewRect, previewCamera, pivotPreviewPosition, gizmoWorldSize, controlId);
        DrawPivotGizmoOverlay(previewRect, previewCamera, pivotPreviewPosition, gizmoWorldSize, controlId);
    }

    private float GetPivotGizmoWorldSize(Camera previewCamera, Vector3 pivotPreviewPosition)
    {
        if (previewCamera == null)
        {
            return 0.5f;
        }

        float distance = Mathf.Max(Vector3.Distance(previewCamera.transform.position, pivotPreviewPosition), 0.1f);
        float height = 2f * distance * Mathf.Tan(previewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        return Mathf.Max(height * 0.08f, 0.05f);
    }

    private void HandlePivotGizmoInput(
        Rect previewRect,
        Camera previewCamera,
        Vector3 pivotPreviewPosition,
        float gizmoWorldSize,
        int controlId)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        bool ownsControl = GUIUtility.hotControl == controlId;
        switch (currentEvent.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (currentEvent.button != 0 || !previewRect.Contains(currentEvent.mousePosition))
                {
                    break;
                }

                PivotDragMode hitMode = PickPivotDragMode(previewRect, previewCamera, pivotPreviewPosition, gizmoWorldSize, currentEvent.mousePosition);
                if (hitMode == PivotDragMode.None)
                {
                    break;
                }

                Undo.RecordObject(this, "Move Mesh Transform Pivot");
                pivotDragMode = hitMode;
                pivotDragStartPreviewPosition = pivotPreviewPosition;
                GUIUtility.hotControl = controlId;

                if (pivotDragMode == PivotDragMode.Plane)
                {
                    if (!TryGetPreviewRay(previewRect, previewCamera, currentEvent.mousePosition, out Ray ray)
                        || !TryIntersectPlane(ray, pivotPreviewPosition, previewCamera.transform.forward, out pivotDragStartPointerPreviewPosition))
                    {
                        pivotDragStartPointerPreviewPosition = pivotPreviewPosition;
                    }
                }
                else
                {
                    pivotDragAxis = GetPivotDragAxis(pivotDragMode);
                    if (!TryGetPreviewRay(previewRect, previewCamera, currentEvent.mousePosition, out Ray ray)
                        || !TryGetAxisParameter(ray, pivotPreviewPosition, pivotDragAxis, out pivotDragStartAxisParameter))
                    {
                        pivotDragStartAxisParameter = 0f;
                    }
                }

                currentEvent.Use();
                break;

            case EventType.MouseDrag:
                if (!ownsControl)
                {
                    break;
                }

                Vector3 nextPreviewPosition = pivotDragStartPreviewPosition;
                if (pivotDragMode == PivotDragMode.Plane)
                {
                    if (TryGetPreviewRay(previewRect, previewCamera, currentEvent.mousePosition, out Ray ray)
                        && TryIntersectPlane(ray, pivotDragStartPreviewPosition, previewCamera.transform.forward, out Vector3 pointerPreviewPosition))
                    {
                        nextPreviewPosition += pointerPreviewPosition - pivotDragStartPointerPreviewPosition;
                    }
                }
                else if (pivotDragMode != PivotDragMode.None)
                {
                    if (TryGetPreviewRay(previewRect, previewCamera, currentEvent.mousePosition, out Ray ray)
                        && TryGetAxisParameter(ray, pivotDragStartPreviewPosition, pivotDragAxis, out float axisParameter))
                    {
                        nextPreviewPosition += pivotDragAxis * (axisParameter - pivotDragStartAxisParameter);
                    }
                }

                pivotPosition = nextPreviewPosition - positionOffset;
                previewDirty = true;
                EditorUtility.SetDirty(this);
                Repaint();
                currentEvent.Use();
                break;

            case EventType.MouseUp:
                if (!ownsControl || currentEvent.button != 0)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                pivotDragMode = PivotDragMode.None;
                currentEvent.Use();
                break;
        }
    }

    private void DrawPivotGizmoOverlay(
        Rect previewRect,
        Camera previewCamera,
        Vector3 pivotPreviewPosition,
        float gizmoWorldSize,
        int controlId)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Vector2 pivotGuiPoint = WorldToPreviewGuiPoint(previewRect, previewCamera, pivotPreviewPosition);
        if (!previewRect.Overlaps(new Rect(pivotGuiPoint.x - 24f, pivotGuiPoint.y - 24f, 48f, 48f)))
        {
            return;
        }

        bool ownsControl = GUIUtility.hotControl == controlId;
        Handles.BeginGUI();
        DrawPivotAxis(previewRect, previewCamera, pivotGuiPoint, pivotPreviewPosition, Vector3.right, gizmoWorldSize, new Color(0.95f, 0.25f, 0.22f, 1f));
        DrawPivotAxis(previewRect, previewCamera, pivotGuiPoint, pivotPreviewPosition, Vector3.up, gizmoWorldSize, new Color(0.25f, 0.85f, 0.32f, 1f));
        DrawPivotAxis(previewRect, previewCamera, pivotGuiPoint, pivotPreviewPosition, Vector3.forward, gizmoWorldSize, new Color(0.25f, 0.48f, 1f, 1f));

        Color previousColor = Handles.color;
        Handles.color = ownsControl ? Color.white : new Color(1f, 0.68f, 0.16f, 1f);
        Handles.DrawSolidDisc(pivotGuiPoint, Vector3.forward, 5f);
        Handles.DrawWireDisc(pivotGuiPoint, Vector3.forward, PivotCenterPickRadius);
        Handles.color = previousColor;

        Rect labelRect = new Rect(pivotGuiPoint.x + 9f, pivotGuiPoint.y - 20f, 48f, 18f);
        GUI.Label(labelRect, "Pivot", EditorStyles.miniBoldLabel);
        Handles.EndGUI();
    }

    private static void DrawPivotAxis(
        Rect previewRect,
        Camera previewCamera,
        Vector2 pivotGuiPoint,
        Vector3 pivotPreviewPosition,
        Vector3 axis,
        float gizmoWorldSize,
        Color color)
    {
        Vector3 axisEnd = pivotPreviewPosition + axis * gizmoWorldSize;
        Vector3 viewportPoint = previewCamera.WorldToViewportPoint(axisEnd);
        if (viewportPoint.z <= 0f)
        {
            return;
        }

        Vector2 axisGuiPoint = WorldToPreviewGuiPoint(previewRect, previewCamera, axisEnd);
        Color previousColor = Handles.color;
        Handles.color = color;
        Handles.DrawAAPolyLine(4f, pivotGuiPoint, axisGuiPoint);
        Handles.DrawSolidDisc(axisGuiPoint, Vector3.forward, 4f);
        Handles.color = previousColor;
    }

    private static PivotDragMode PickPivotDragMode(
        Rect previewRect,
        Camera previewCamera,
        Vector3 pivotPreviewPosition,
        float gizmoWorldSize,
        Vector2 mousePosition)
    {
        Vector2 pivotGuiPoint = WorldToPreviewGuiPoint(previewRect, previewCamera, pivotPreviewPosition);
        if (Vector2.Distance(mousePosition, pivotGuiPoint) <= PivotCenterPickRadius)
        {
            return PivotDragMode.Plane;
        }

        if (IsNearPivotAxis(previewRect, previewCamera, pivotGuiPoint, pivotPreviewPosition, Vector3.right, gizmoWorldSize, mousePosition))
        {
            return PivotDragMode.AxisX;
        }

        if (IsNearPivotAxis(previewRect, previewCamera, pivotGuiPoint, pivotPreviewPosition, Vector3.up, gizmoWorldSize, mousePosition))
        {
            return PivotDragMode.AxisY;
        }

        if (IsNearPivotAxis(previewRect, previewCamera, pivotGuiPoint, pivotPreviewPosition, Vector3.forward, gizmoWorldSize, mousePosition))
        {
            return PivotDragMode.AxisZ;
        }

        return PivotDragMode.None;
    }

    private static bool IsNearPivotAxis(
        Rect previewRect,
        Camera previewCamera,
        Vector2 pivotGuiPoint,
        Vector3 pivotPreviewPosition,
        Vector3 axis,
        float gizmoWorldSize,
        Vector2 mousePosition)
    {
        Vector3 axisEnd = pivotPreviewPosition + axis * gizmoWorldSize;
        if (previewCamera.WorldToViewportPoint(axisEnd).z <= 0f)
        {
            return false;
        }

        Vector2 axisGuiPoint = WorldToPreviewGuiPoint(previewRect, previewCamera, axisEnd);
        return DistanceToSegment(mousePosition, pivotGuiPoint, axisGuiPoint) <= PivotAxisPickRadius;
    }

    private static Vector3 GetPivotDragAxis(PivotDragMode dragMode)
    {
        switch (dragMode)
        {
            case PivotDragMode.AxisX:
                return Vector3.right;
            case PivotDragMode.AxisY:
                return Vector3.up;
            case PivotDragMode.AxisZ:
                return Vector3.forward;
            default:
                return Vector3.right;
        }
    }

    private static Vector2 WorldToPreviewGuiPoint(Rect previewRect, Camera previewCamera, Vector3 worldPosition)
    {
        Vector3 viewportPoint = previewCamera.WorldToViewportPoint(worldPosition);
        return new Vector2(
            previewRect.x + viewportPoint.x * previewRect.width,
            previewRect.y + (1f - viewportPoint.y) * previewRect.height);
    }

    private static bool TryGetPreviewRay(Rect previewRect, Camera previewCamera, Vector2 guiPosition, out Ray ray)
    {
        if (previewCamera == null || previewRect.width <= 0f || previewRect.height <= 0f)
        {
            ray = default;
            return false;
        }

        Vector3 viewportPoint = new Vector3(
            Mathf.InverseLerp(previewRect.xMin, previewRect.xMax, guiPosition.x),
            1f - Mathf.InverseLerp(previewRect.yMin, previewRect.yMax, guiPosition.y),
            0f);
        ray = previewCamera.ViewportPointToRay(viewportPoint);
        return true;
    }

    private static bool TryIntersectPlane(Ray ray, Vector3 planePoint, Vector3 planeNormal, out Vector3 hitPoint)
    {
        Plane plane = new Plane(planeNormal.normalized, planePoint);
        if (!plane.Raycast(ray, out float distance))
        {
            hitPoint = default;
            return false;
        }

        hitPoint = ray.GetPoint(distance);
        return true;
    }

    private static bool TryGetAxisParameter(Ray ray, Vector3 axisOrigin, Vector3 axisDirection, out float parameter)
    {
        Vector3 axis = axisDirection.normalized;
        Vector3 rayDirection = ray.direction.normalized;
        Vector3 originDelta = axisOrigin - ray.origin;
        float axisDotRay = Vector3.Dot(axis, rayDirection);
        float denominator = 1f - axisDotRay * axisDotRay;
        if (Mathf.Abs(denominator) < 0.0001f)
        {
            parameter = 0f;
            return false;
        }

        float axisDotDelta = Vector3.Dot(axis, originDelta);
        float rayDotDelta = Vector3.Dot(rayDirection, originDelta);
        parameter = (axisDotRay * rayDotDelta - axisDotDelta) / denominator;
        return true;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
    {
        Vector2 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            return Vector2.Distance(point, segmentStart);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
        Vector2 projectedPoint = segmentStart + segment * t;
        return Vector2.Distance(point, projectedPoint);
    }
}
