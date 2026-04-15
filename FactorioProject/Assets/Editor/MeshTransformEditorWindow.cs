using System.IO;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

public class MeshTransformEditorWindow : EditorWindow
{
    private const float PresetButtonHeight = 24f;
    private const float PreviewHeight = 260f;

    [SerializeField]
    private Vector2 scrollPosition;
    [SerializeField]
    private Mesh sourceMesh;
    [SerializeField]
    private bool syncSelection = true;
    [SerializeField]
    private Vector3 positionOffset = Vector3.zero;
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

    private PreviewRenderUtility previewRenderer;
    private Material previewMaterial;
    private Mesh previewMesh;
    private Mesh previewMeshSource;
    private bool previewDirty = true;

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
        sourceMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", sourceMesh, typeof(Mesh), false);
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
                rotationEuler = Vector3.zero;
                scale = Vector3.one;
                previewDirty = true;
            }
        }

        if (sourceMesh == null)
        {
            EditorGUILayout.HelpBox("Project 창의 Mesh Asset 또는 MeshFilter가 있는 오브젝트를 선택하면 자동으로 가져옵니다.", MessageType.Warning);
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(sourceMesh);
        EditorGUILayout.LabelField("Path", string.IsNullOrWhiteSpace(assetPath) ? "(Scene Mesh)" : assetPath, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Vertices", sourceMesh.vertexCount.ToString());
        EditorGUILayout.LabelField("Bounds Center", sourceMesh.bounds.center.ToString("F3"));
        EditorGUILayout.LabelField("Bounds Size", sourceMesh.bounds.size.ToString("F3"));
    }

    private void DrawTransformSection()
    {
        EditorGUILayout.LabelField("Transform", EditorStyles.boldLabel);
        positionOffset = EditorGUILayout.Vector3Field("Position", positionOffset);
        rotationEuler = EditorGUILayout.Vector3Field("Rotation", rotationEuler);
        scale = EditorGUILayout.Vector3Field("Scale", scale);

        if (sourceMesh == null)
        {
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Pivot Preset", EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Center", GUILayout.Height(PresetButtonHeight)))
            {
                positionOffset = -sourceMesh.bounds.center;
                previewDirty = true;
            }

            if (GUILayout.Button("Bottom", GUILayout.Height(PresetButtonHeight)))
            {
                Bounds bounds = sourceMesh.bounds;
                positionOffset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                previewDirty = true;
            }

            if (GUILayout.Button("Top", GUILayout.Height(PresetButtonHeight)))
            {
                Bounds bounds = sourceMesh.bounds;
                positionOffset = new Vector3(-bounds.center.x, -bounds.max.y, -bounds.center.z);
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

        if (sourceMesh == null)
        {
            EditorGUI.DropShadowLabel(previewRect, "Preview unavailable");
            return;
        }

        HandlePreviewInput(previewRect);
        DrawMeshPreview(previewRect);
    }

    private void DrawSaveButtons()
    {
        string sourcePath = sourceMesh != null ? AssetDatabase.GetAssetPath(sourceMesh) : string.Empty;
        bool canSave = CanSaveToSource(sourcePath, out string saveInfoMessage);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(!canSave);
            if (GUILayout.Button("Save", GUILayout.Height(34f)))
            {
                SaveToSourceMesh();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(sourceMesh == null);
            if (GUILayout.Button("Save As", GUILayout.Height(34f)))
            {
                CreateTransformedMeshAsset();
            }
            EditorGUI.EndDisabledGroup();
        }

        if (sourceMesh != null && string.IsNullOrWhiteSpace(sourcePath))
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

        Mesh selectedMesh = ResolveSelectedMesh();
        if (selectedMesh != null)
        {
            if (sourceMesh != selectedMesh)
            {
                previewDirty = true;
            }

            sourceMesh = selectedMesh;
        }
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

    private void SaveToSourceMesh()
    {
        if (sourceMesh == null)
        {
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(sourceMesh);
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

        if (sourceMesh == null || string.IsNullOrWhiteSpace(sourcePath))
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
                if (CanOverwriteFbxSource(sourcePath, out string fbxReason))
                {
                    message = "Save를 누르면 원본 FBX 파일을 정적 메쉬 기준으로 다시 내보냅니다.";
                    return true;
                }

                message = fbxReason;
                return false;

            default:
                message = "Save를 누르면 현재 Mesh Asset 데이터를 저장합니다.";
                return true;
        }
    }

    private bool CanOverwriteFbxSource(string sourcePath, out string reason)
    {
        reason = string.Empty;

        GameObject modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (modelRoot == null)
        {
            reason = "이 FBX는 모델 루트를 찾을 수 없어 원본 덮어쓰기를 지원하지 않습니다.";
            return false;
        }

        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters == null || meshFilters.Length != 1)
        {
            reason = "여러 메쉬가 들어있는 FBX는 원본 덮어쓰기를 막았습니다. Save As를 사용하세요.";
            return false;
        }

        if (modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
        {
            reason = "SkinnedMeshRenderer가 있는 FBX는 리그 손상 위험 때문에 원본 덮어쓰기를 막았습니다.";
            return false;
        }

        if (modelRoot.GetComponentsInChildren<Animator>(true).Length > 0
            || modelRoot.GetComponentsInChildren<Animation>(true).Length > 0)
        {
            reason = "애니메이션이 있는 FBX는 원본 덮어쓰기를 막았습니다. Save As를 사용하세요.";
            return false;
        }

        int meshAssetCount = 0;
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh)
            {
                meshAssetCount++;
            }
        }

        if (meshAssetCount != 1)
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
        if (!CanOverwriteFbxSource(sourcePath, out string reason))
        {
            EditorUtility.DisplayDialog("Mesh Transform", reason, "OK");
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

    private void ApplyTransform(Mesh mesh, bool markDirty = true)
    {
        Quaternion rotation = Quaternion.Euler(rotationEuler);
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 transformedVertex = Vector3.Scale(vertices[i], scale);
            transformedVertex = rotation * transformedVertex;
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

        Shader previewShader = Shader.Find("Standard");
        if (previewShader == null)
        {
            previewShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (previewShader == null)
        {
            previewShader = Shader.Find("Legacy Shaders/Diffuse");
        }

        previewMaterial = new Material(previewShader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            color = new Color(0.78f, 0.82f, 0.88f, 1f)
        };
    }

    private void DisposePreviewResources()
    {
        if (previewMesh != null)
        {
            DestroyImmediate(previewMesh);
            previewMesh = null;
        }

        previewMeshSource = null;

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

        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Bounds bounds = previewMesh.bounds;
        Vector3 center = bounds.center;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
        float distance = radius * Mathf.Max(1.8f, previewZoom * 2.4f);

        Quaternion orbitRotation = Quaternion.Euler(previewOrbit.y, previewOrbit.x, 0f);
        Vector3 cameraOffset = orbitRotation * (Vector3.back * distance);

        previewRenderer.BeginPreview(previewRect, GUIStyle.none);
        previewRenderer.camera.transform.position = center + cameraOffset;
        previewRenderer.camera.transform.LookAt(center);
        previewRenderer.camera.nearClipPlane = 0.01f;
        previewRenderer.camera.farClipPlane = Mathf.Max(100f, distance + radius * 6f);

        for (int subMeshIndex = 0; subMeshIndex < previewMesh.subMeshCount; subMeshIndex++)
        {
            previewRenderer.DrawMesh(previewMesh, Matrix4x4.identity, previewMaterial, subMeshIndex);
        }

        previewRenderer.Render();
        Texture previewTexture = previewRenderer.EndPreview();
        GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, false);
    }

    private void UpdatePreviewMeshIfNeeded()
    {
        if (!previewDirty && previewMesh != null && previewMeshSource == sourceMesh)
        {
            return;
        }

        if (previewMesh != null)
        {
            DestroyImmediate(previewMesh);
            previewMesh = null;
        }

        previewMeshSource = sourceMesh;
        if (sourceMesh == null)
        {
            previewDirty = false;
            return;
        }

        previewMesh = Instantiate(sourceMesh);
        previewMesh.name = $"{sourceMesh.name}_Preview";
        previewMesh.hideFlags = HideFlags.HideAndDontSave;
        ApplyTransform(previewMesh, false);
        previewDirty = false;
    }
}
