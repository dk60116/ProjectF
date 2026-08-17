using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class MeshCombineEditorWindow : EditorWindow
{
    private const float PreviewHeight = 260f;
    private const string ImportedMeshFolder = "Assets/MeshCombineImports";
    private static readonly Color PreviewMaterialColor = new Color(0.76f, 0.72f, 0.64f, 1f);
    private static readonly string[] PreviewShaderNames =
    {
        "Custom/ToonCharacter",
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Lit",
        "Unlit/Color",
        "Legacy Shaders/Diffuse",
        "Standard",
        "Hidden/Internal-Colored"
    };

    [Serializable]
    private class MeshEntry
    {
        public bool enabled = true;
        public Mesh mesh;
        public string displayName = "Mesh";
        public Vector3 position = Vector3.zero;
        public Vector3 rotationEuler = Vector3.zero;
        public Vector3 scale = Vector3.one;
    }

    [SerializeField]
    private List<MeshEntry> meshEntries = new List<MeshEntry>();
    [SerializeField]
    private Vector2 scrollPosition;
    [SerializeField]
    private Vector2 previewOrbit = new Vector2(135f, -20f);
    [SerializeField]
    private float previewZoom = 1.35f;
    [SerializeField]
    private Vector3 pivotPosition = Vector3.zero;
    [SerializeField]
    private bool showPivotInPreview = true;
    private bool recalculateNormals = false;
    [SerializeField]
    private bool recalculateTangents = false;
    [SerializeField]
    private string outputFolder = "Assets";
    [SerializeField]
    private string outputName = "CombinedMesh";

    private PreviewRenderUtility previewRenderer;
    private Material previewMaterial;
    private Mesh previewMesh;
    private bool previewDirty = true;
    private bool combinedBoundsDirty = true;
    private Bounds cachedCombinedBounds;
    private bool cachedCombinedBoundsValid;

    [MenuItem("Window/ProjectF/Mesh Combine")]
    [MenuItem("Tools/MapObject/Mesh Combine")]
    public static void ShowWindow()
    {
        MeshCombineEditorWindow window = GetWindow<MeshCombineEditorWindow>("Mesh Combine");
        window.minSize = new Vector2(460f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        EnsurePreviewRenderer();
    }

    private void OnDisable()
    {
        DisposePreviewResources();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Mesh Combine", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("2개 이상의 Mesh를 불러와 각 Transform을 조정한 뒤, Pivot을 기준으로 하나의 Mesh Asset으로 저장합니다.", MessageType.Info);

        DrawPreviewSection();
        EditorGUILayout.Space(8f);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUI.BeginChangeCheck();

        DrawSourceSection();
        EditorGUILayout.Space(8f);
        DrawPivotSection();
        EditorGUILayout.Space(8f);
        DrawOutputSection();

        if (EditorGUI.EndChangeCheck())
        {
            MarkPreviewDirty();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSourceSection()
    {
        EditorGUILayout.LabelField("Sources", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Project/Hierarchy에서 Mesh, FBX, OBJ, Prefab, MeshFilter 오브젝트를 선택하거나 파일을 드래그해서 추가합니다. Assets 밖 파일은 Assets/MeshCombineImports로 복사해 임포트합니다.", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Empty Slot", GUILayout.Height(24f)))
        {
            Undo.RecordObject(this, "Add Mesh Combine Slot");
            meshEntries.Add(new MeshEntry());
            MarkPreviewDirty();
        }

        if (GUILayout.Button("Add Selected", GUILayout.Height(24f)))
        {
            AddSelection();
        }

        using (new EditorGUI.DisabledScope(meshEntries.Count == 0))
        {
            if (GUILayout.Button("Clear", GUILayout.Height(24f), GUILayout.Width(72f)))
            {
                Undo.RecordObject(this, "Clear Mesh Combine Sources");
                meshEntries.Clear();
                MarkPreviewDirty();
            }
        }
        EditorGUILayout.EndHorizontal();

        Rect dropRect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drag .mesh / .fbx / .obj / Prefab / GameObject Here", EditorStyles.helpBox);
        HandleSourceDrop(dropRect);

        EditorGUILayout.Space(4f);
        for (int i = 0; i < meshEntries.Count; i++)
        {
            DrawEntry(i);
            EditorGUILayout.Space(6f);
        }
    }

    private void DrawEntry(int index)
    {
        MeshEntry entry = meshEntries[index];
        if (entry == null)
        {
            meshEntries[index] = new MeshEntry();
            entry = meshEntries[index];
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(18f));
        EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(entry.displayName) ? $"Mesh {index + 1}" : entry.displayName, EditorStyles.boldLabel);

        if (GUILayout.Button("Reset", GUILayout.Width(52f)))
        {
            Undo.RecordObject(this, "Reset Mesh Combine Transform");
            entry.position = Vector3.zero;
            entry.rotationEuler = Vector3.zero;
            entry.scale = Vector3.one;
            MarkPreviewDirty();
        }

        if (GUILayout.Button("Up", GUILayout.Width(36f)) && index > 0)
        {
            Undo.RecordObject(this, "Move Mesh Combine Source");
            (meshEntries[index - 1], meshEntries[index]) = (meshEntries[index], meshEntries[index - 1]);
            MarkPreviewDirty();
        }

        if (GUILayout.Button("Down", GUILayout.Width(50f)) && index + 1 < meshEntries.Count)
        {
            Undo.RecordObject(this, "Move Mesh Combine Source");
            (meshEntries[index + 1], meshEntries[index]) = (meshEntries[index], meshEntries[index + 1]);
            MarkPreviewDirty();
        }

        if (GUILayout.Button("X", GUILayout.Width(26f)))
        {
            Undo.RecordObject(this, "Remove Mesh Combine Source");
            meshEntries.RemoveAt(index);
            MarkPreviewDirty();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        Mesh nextMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", entry.mesh, typeof(Mesh), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(this, "Assign Mesh Combine Source");
            entry.mesh = nextMesh;
            entry.displayName = nextMesh != null ? nextMesh.name : $"Mesh {index + 1}";
            MarkPreviewDirty();
        }

        entry.position = EditorGUILayout.Vector3Field("Position", entry.position);
        entry.rotationEuler = EditorGUILayout.Vector3Field("Rotation", entry.rotationEuler);
        entry.scale = EditorGUILayout.Vector3Field("Scale", entry.scale);
        EditorGUILayout.EndVertical();
    }

    private void DrawPivotSection()
    {
        EditorGUILayout.LabelField("Pivot", EditorStyles.boldLabel);
        pivotPosition = EditorGUILayout.Vector3Field("Pivot Position", pivotPosition);
        showPivotInPreview = EditorGUILayout.ToggleLeft("Preview에 Pivot 표시", showPivotInPreview);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Origin"))
        {
            Undo.RecordObject(this, "Set Mesh Combine Pivot");
            pivotPosition = Vector3.zero;
            MarkPreviewDirty();
        }

        using (new EditorGUI.DisabledScope(!TryGetCombinedBounds(out Bounds bounds)))
        {
            if (GUILayout.Button("Bounds Center"))
            {
                Undo.RecordObject(this, "Set Mesh Combine Pivot");
                pivotPosition = bounds.center;
                MarkPreviewDirty();
            }

            if (GUILayout.Button("Bottom Center"))
            {
                Undo.RecordObject(this, "Set Mesh Combine Pivot");
                pivotPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                MarkPreviewDirty();
            }

            if (GUILayout.Button("Top Center"))
            {
                Undo.RecordObject(this, "Set Mesh Combine Pivot");
                pivotPosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                MarkPreviewDirty();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("저장 시 모든 버텍스에서 Pivot Position을 빼서, 생성된 Mesh의 원점이 Pivot이 됩니다.", MessageType.None);
    }

    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("출력 Mesh는 모든 입력 Mesh와 SubMesh를 하나의 Mesh / 하나의 SubMesh로 완전 병합합니다.", MessageType.None);
        recalculateNormals = EditorGUILayout.ToggleLeft("Normals 재계산", recalculateNormals);
        recalculateTangents = EditorGUILayout.ToggleLeft("Tangents 재계산", recalculateTangents);

        EditorGUILayout.BeginHorizontal();
        Rect outputFolderRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        outputFolder = EditorGUI.TextField(outputFolderRect, "Folder", outputFolder);
        HandleOutputFolderDrop(outputFolderRect);
        if (GUILayout.Button("...", GUILayout.Width(32f)))
        {
            string absolutePath = EditorUtility.OpenFolderPanel("Output Folder", Application.dataPath, string.Empty);
            if (!string.IsNullOrEmpty(absolutePath))
            {
                string relativePath = AbsolutePathToAssetPath(absolutePath);
                if (!string.IsNullOrEmpty(relativePath))
                {
                    outputFolder = relativePath;
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Assets 폴더 아래 경로를 선택해주세요.", "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("Project 창의 Folder를 Folder 필드에 드래그해서 Output Folder로 지정할 수 있습니다.", MessageType.None);

        outputName = EditorGUILayout.TextField("File Name", outputName);

        int validSourceCount = CountValidSources();
        using (new EditorGUI.DisabledScope(validSourceCount < 2))
        {
            if (GUILayout.Button("Create Combined Mesh", GUILayout.Height(30f)))
            {
                CreateCombinedMeshAsset();
            }
        }

        if (validSourceCount < 2)
        {
            EditorGUILayout.HelpBox("저장하려면 활성화된 Mesh가 2개 이상 필요합니다.", MessageType.Warning);
        }
    }

    private void DrawPreviewSection()
    {
        Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        GUI.Box(previewRect, GUIContent.none);
        HandlePreviewInput(previewRect);
        DrawMeshPreview(previewRect);
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
                currentEvent.Use();
                break;

            case EventType.MouseDrag:
                if (currentEvent.button != 0 && currentEvent.button != 1)
                {
                    break;
                }

                previewOrbit.x += currentEvent.delta.x;
                previewOrbit.y = Mathf.Clamp(previewOrbit.y - currentEvent.delta.y, -89f, 89f);
                currentEvent.Use();
                break;
        }
    }

    private void DrawMeshPreview(Rect previewRect)
    {
        EnsurePreviewRenderer();
        UpdatePreviewMeshIfNeeded();

        if (previewRenderer == null || previewMaterial == null || previewMesh == null || previewMesh.vertexCount == 0)
        {
            EditorGUI.DropShadowLabel(previewRect, "Add 2 or more meshes");
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

        if (Event.current.type == EventType.Repaint)
        {
            previewRenderer.BeginPreview(previewRect, GUIStyle.none);
            for (int subMeshIndex = 0; subMeshIndex < previewMesh.subMeshCount; subMeshIndex++)
            {
                previewRenderer.DrawMesh(previewMesh, Matrix4x4.identity, previewMaterial, subMeshIndex);
            }

            previewRenderer.Render(true, true);
            Texture previewTexture = previewRenderer.EndPreview();
            GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, false);
        }

        DrawPivotOverlay(previewRect, previewRenderer.camera);
    }

    private void DrawPivotOverlay(Rect previewRect, Camera previewCamera)
    {
        if (!showPivotInPreview || previewCamera == null)
        {
            return;
        }

        Vector2 pivotPoint = WorldToPreviewGuiPoint(previewRect, previewCamera, pivotPosition, out bool visible);
        if (!visible)
        {
            return;
        }

        Handles.BeginGUI();
        Color oldColor = Handles.color;
        Handles.color = new Color(1f, 0.82f, 0.16f, 1f);
        Handles.DrawSolidDisc(pivotPoint, Vector3.forward, 5f);
        Handles.DrawWireDisc(pivotPoint, Vector3.forward, 12f);
        DrawPivotAxis(previewRect, previewCamera, pivotPoint, Vector3.right, new Color(0.95f, 0.25f, 0.22f, 1f));
        DrawPivotAxis(previewRect, previewCamera, pivotPoint, Vector3.up, new Color(0.25f, 0.85f, 0.32f, 1f));
        DrawPivotAxis(previewRect, previewCamera, pivotPoint, Vector3.forward, new Color(0.25f, 0.48f, 1f, 1f));
        Handles.color = oldColor;
        Handles.EndGUI();

        GUI.Label(new Rect(pivotPoint.x + 10f, pivotPoint.y - 18f, 60f, 18f), "Pivot", EditorStyles.miniBoldLabel);
    }

    private void DrawPivotAxis(Rect previewRect, Camera previewCamera, Vector2 pivotPoint, Vector3 axis, Color color)
    {
        Vector2 axisPoint = WorldToPreviewGuiPoint(previewRect, previewCamera, pivotPosition + axis * GetPivotGizmoSize(), out bool visible);
        if (!visible)
        {
            return;
        }

        Color oldColor = Handles.color;
        Handles.color = color;
        Handles.DrawAAPolyLine(4f, pivotPoint, axisPoint);
        Handles.DrawSolidDisc(axisPoint, Vector3.forward, 3.5f);
        Handles.color = oldColor;
    }

    private float GetPivotGizmoSize()
    {
        if (TryGetCombinedBounds(out Bounds bounds))
        {
            return Mathf.Max(bounds.extents.magnitude * 0.18f, 0.08f);
        }

        return 0.25f;
    }

    private static Vector2 WorldToPreviewGuiPoint(Rect previewRect, Camera previewCamera, Vector3 worldPosition, out bool visible)
    {
        Vector3 screenPoint = previewCamera.WorldToScreenPoint(worldPosition);
        visible = screenPoint.z > 0f;
        if (!visible)
        {
            return Vector2.zero;
        }

        float pixelWidth = Mathf.Max(1f, previewCamera.pixelWidth);
        float pixelHeight = Mathf.Max(1f, previewCamera.pixelHeight);
        return new Vector2(
            previewRect.x + screenPoint.x / pixelWidth * previewRect.width,
            previewRect.y + (1f - screenPoint.y / pixelHeight) * previewRect.height);
    }

    private void AddSelection()
    {
        UnityEngine.Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Mesh Combine", "선택된 Mesh / FBX / Prefab / GameObject가 없습니다.", "OK");
            return;
        }

        Undo.RecordObject(this, "Add Selected Mesh Combine Sources");
        int addedCount = 0;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            addedCount += AddSourceObject(selectedObjects[i]);
        }

        if (addedCount == 0)
        {
            EditorUtility.DisplayDialog("Mesh Combine", "선택 항목에서 Mesh를 찾지 못했습니다.", "OK");
            return;
        }

        MarkPreviewDirty();
    }

    private void HandleSourceDrop(Rect dropRect)
    {
        Event currentEvent = Event.current;
        if (!dropRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            Undo.RecordObject(this, "Drop Mesh Combine Sources");
            int addedCount = 0;
            UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
            HashSet<string> handledAssetPaths = new HashSet<string>();
            for (int i = 0; i < draggedObjects.Length; i++)
            {
                string assetPath = AssetDatabase.GetAssetPath(draggedObjects[i]);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    handledAssetPaths.Add(NormalizePathSeparators(assetPath));
                }

                addedCount += AddSourceObject(draggedObjects[i]);
            }

            string[] draggedPaths = DragAndDrop.paths;
            for (int i = 0; i < draggedPaths.Length; i++)
            {
                addedCount += AddSourcePath(draggedPaths[i], handledAssetPaths);
            }

            if (addedCount > 0)
            {
                MarkPreviewDirty();
            }
        }

        currentEvent.Use();
    }

    private void HandleOutputFolderDrop(Rect dropRect)
    {
        Event currentEvent = Event.current;
        if (!dropRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
        {
            return;
        }

        string draggedFolderPath = GetDraggedProjectFolderPath();
        if (string.IsNullOrEmpty(draggedFolderPath))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            currentEvent.Use();
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Link;
        if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            Undo.RecordObject(this, "Set Mesh Combine Output Folder");
            outputFolder = draggedFolderPath;
            Repaint();
        }

        currentEvent.Use();
    }

    private static string GetDraggedProjectFolderPath()
    {
        UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
        for (int i = 0; i < draggedObjects.Length; i++)
        {
            string assetPath = AssetDatabase.GetAssetPath(draggedObjects[i]);
            if (TryNormalizeProjectFolderPath(assetPath, out string folderPath))
            {
                return folderPath;
            }
        }

        string[] draggedPaths = DragAndDrop.paths;
        for (int i = 0; i < draggedPaths.Length; i++)
        {
            if (TryNormalizeProjectFolderPath(draggedPaths[i], out string folderPath))
            {
                return folderPath;
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeProjectFolderPath(string path, out string folderPath)
    {
        folderPath = string.Empty;
        string assetPath = NormalizeToAssetPath(path);
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        assetPath = NormalizePathSeparators(assetPath).TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(assetPath))
        {
            return false;
        }

        folderPath = assetPath;
        return true;
    }

    private int AddSourcePath(string sourcePath, HashSet<string> handledAssetPaths)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return 0;
        }

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!IsSupportedMeshFileExtension(extension))
        {
            return 0;
        }

        string assetPath = NormalizeToAssetPath(sourcePath);
        if (string.IsNullOrEmpty(assetPath))
        {
            assetPath = ImportExternalMeshFile(sourcePath);
        }

        if (string.IsNullOrEmpty(assetPath))
        {
            return 0;
        }

        assetPath = NormalizePathSeparators(assetPath);
        if (handledAssetPaths != null && handledAssetPaths.Contains(assetPath))
        {
            return 0;
        }

        return AddSourceAssetAtPath(assetPath);
    }

    private int AddSourceAssetAtPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return 0;
        }

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        int addedCount = AddSourceObject(mainAsset);
        if (addedCount > 0)
        {
            return addedCount;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (assets == null || assets.Length == 0)
        {
            return 0;
        }

        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh mesh)
            {
                AddEntry(mesh, $"{Path.GetFileNameWithoutExtension(assetPath)}/{mesh.name}", Vector3.zero, Vector3.zero, Vector3.one);
                addedCount++;
            }
        }

        return addedCount;
    }

    private string ImportExternalMeshFile(string sourcePath)
    {
        string normalizedSourcePath = NormalizePathSeparators(sourcePath);
        if (!File.Exists(normalizedSourcePath))
        {
            return string.Empty;
        }

        EnsureImportedMeshFolder();
        string fileName = MakeSafeFileName(Path.GetFileName(normalizedSourcePath));
        string targetAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{ImportedMeshFolder}/{fileName}");
        string targetAbsolutePath = AssetPathToAbsolutePath(targetAssetPath);
        if (string.IsNullOrEmpty(targetAbsolutePath))
        {
            return string.Empty;
        }

        File.Copy(normalizedSourcePath, targetAbsolutePath, false);
        TryCopyObjMaterialFiles(normalizedSourcePath, targetAbsolutePath);
        AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        return targetAssetPath;
    }

    private static void TryCopyObjMaterialFiles(string sourcePath, string targetPath)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), ".obj", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        HashSet<string> materialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddObjMaterialPath(materialPaths, Path.ChangeExtension(sourcePath, ".mtl"));

        try
        {
            string sourceDirectory = Path.GetDirectoryName(sourcePath);
            foreach (string line in File.ReadLines(sourcePath))
            {
                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string materialName = trimmedLine.Substring(7).Trim().Trim('"');
                if (string.IsNullOrEmpty(materialName))
                {
                    continue;
                }

                string materialPath = Path.IsPathRooted(materialName) ? materialName : Path.Combine(sourceDirectory, materialName);
                AddObjMaterialPath(materialPaths, materialPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        string targetDirectory = Path.GetDirectoryName(targetPath);
        foreach (string materialPath in materialPaths)
        {
            string targetMaterialPath = Path.Combine(targetDirectory, Path.GetFileName(materialPath));
            if (File.Exists(targetMaterialPath))
            {
                continue;
            }

            File.Copy(materialPath, targetMaterialPath, false);
        }
    }

    private static void AddObjMaterialPath(HashSet<string> materialPaths, string materialPath)
    {
        if (string.IsNullOrEmpty(materialPath) || !File.Exists(materialPath))
        {
            return;
        }

        materialPaths.Add(materialPath);
    }

    private static bool IsSupportedMeshFileExtension(string extension)
    {
        return string.Equals(extension, ".mesh", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase);
    }

    private int AddSourceObject(UnityEngine.Object sourceObject)
    {
        if (sourceObject == null)
        {
            return 0;
        }

        if (sourceObject is Mesh mesh)
        {
            AddEntry(mesh, mesh.name, Vector3.zero, Vector3.zero, Vector3.one);
            return 1;
        }

        GameObject gameObject = ResolveGameObject(sourceObject);
        if (gameObject == null)
        {
            return 0;
        }

        int addedCount = 0;
        Transform root = gameObject.transform;
        MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            Mesh childMesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (childMesh == null)
            {
                continue;
            }

            Matrix4x4 localToRoot = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
            DecomposeMatrix(localToRoot, out Vector3 position, out Vector3 rotationEuler, out Vector3 scale);
            AddEntry(childMesh, $"{gameObject.name}/{meshFilter.name}", position, rotationEuler, scale);
            addedCount++;
        }

        SkinnedMeshRenderer[] skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            SkinnedMeshRenderer skinnedMeshRenderer = skinnedMeshRenderers[i];
            Mesh childMesh = skinnedMeshRenderer != null ? skinnedMeshRenderer.sharedMesh : null;
            if (childMesh == null)
            {
                continue;
            }

            Matrix4x4 localToRoot = root.worldToLocalMatrix * skinnedMeshRenderer.transform.localToWorldMatrix;
            DecomposeMatrix(localToRoot, out Vector3 position, out Vector3 rotationEuler, out Vector3 scale);
            AddEntry(childMesh, $"{gameObject.name}/{skinnedMeshRenderer.name}", position, rotationEuler, scale);
            addedCount++;
        }

        return addedCount;
    }

    private static GameObject ResolveGameObject(UnityEngine.Object sourceObject)
    {
        if (sourceObject is GameObject gameObject)
        {
            return gameObject;
        }

        if (sourceObject is Component component)
        {
            return component.gameObject;
        }

        return null;
    }

    private void AddEntry(Mesh mesh, string displayName, Vector3 position, Vector3 rotationEuler, Vector3 scale)
    {
        if (mesh == null)
        {
            return;
        }

        meshEntries.Add(new MeshEntry
        {
            enabled = true,
            mesh = mesh,
            displayName = string.IsNullOrWhiteSpace(displayName) ? mesh.name : displayName,
            position = position,
            rotationEuler = rotationEuler,
            scale = SanitizeScale(scale)
        });
    }

    private void CreateCombinedMeshAsset()
    {
        if (CountValidSources() < 2)
        {
            EditorUtility.DisplayDialog("Mesh Combine", "활성화된 Mesh가 2개 이상 필요합니다.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
        {
            EditorUtility.DisplayDialog("Mesh Combine", "유효한 Assets 폴더를 지정해주세요.", "OK");
            return;
        }

        string safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(outputName) ? "CombinedMesh" : outputName);
        string outputPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{safeName}.mesh");
        Mesh combinedMesh;
        try
        {
            combinedMesh = BuildCombinedMesh(true);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Mesh Combine Failed", exception.Message, "OK");
            return;
        }

        combinedMesh.name = safeName;
        AssetDatabase.CreateAsset(combinedMesh, outputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        UnityEngine.Object createdAsset = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
        Selection.activeObject = createdAsset;
        EditorGUIUtility.PingObject(createdAsset);
    }

    private Mesh BuildCombinedMesh(bool bakePivotToOrigin)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector4> tangents = new List<Vector4>();
        List<Vector2> uv0 = new List<Vector2>();
        List<Vector2> uv1 = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<List<int>> subMeshTriangles = new List<List<int>>();

        bool allNormalsValid = true;
        bool allTangentsValid = true;
        bool anyColors = false;

        for (int entryIndex = 0; entryIndex < meshEntries.Count; entryIndex++)
        {
            MeshEntry entry = meshEntries[entryIndex];
            if (!IsValidEntry(entry))
            {
                continue;
            }

            Mesh sourceMesh = entry.mesh;
            Vector3[] sourceVertices = sourceMesh.vertices;
            if (sourceVertices == null || sourceVertices.Length == 0)
            {
                continue;
            }

            int vertexOffset = vertices.Count;
            Matrix4x4 matrix = Matrix4x4.TRS(entry.position, Quaternion.Euler(entry.rotationEuler), SanitizeScale(entry.scale));
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            bool isMirrored = matrix.determinant < 0f;

            Vector3[] sourceNormals = sourceMesh.normals;
            Vector4[] sourceTangents = sourceMesh.tangents;
            Vector2[] sourceUv0 = sourceMesh.uv;
            Vector2[] sourceUv1 = sourceMesh.uv2;
            Color[] sourceColors = sourceMesh.colors;

            bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents != null && sourceTangents.Length == sourceVertices.Length;
            bool hasUv0 = sourceUv0 != null && sourceUv0.Length == sourceVertices.Length;
            bool hasUv1 = sourceUv1 != null && sourceUv1.Length == sourceVertices.Length;
            bool hasColors = sourceColors != null && sourceColors.Length == sourceVertices.Length;

            allNormalsValid &= hasNormals;
            allTangentsValid &= hasTangents;
            anyColors |= hasColors;

            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                Vector3 transformedVertex = matrix.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                if (bakePivotToOrigin)
                {
                    transformedVertex -= pivotPosition;
                }

                vertices.Add(transformedVertex);

                if (hasNormals)
                {
                    normals.Add(normalMatrix.MultiplyVector(sourceNormals[vertexIndex]).normalized);
                }
                else
                {
                    normals.Add(Vector3.up);
                }

                if (hasTangents)
                {
                    Vector4 sourceTangent = sourceTangents[vertexIndex];
                    Vector3 transformedTangent = matrix.MultiplyVector(new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z)).normalized;
                    tangents.Add(new Vector4(
                        transformedTangent.x,
                        transformedTangent.y,
                        transformedTangent.z,
                        isMirrored ? -sourceTangent.w : sourceTangent.w));
                }
                else
                {
                    tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                }

                uv0.Add(hasUv0 ? sourceUv0[vertexIndex] : Vector2.zero);
                uv1.Add(hasUv1 ? sourceUv1[vertexIndex] : Vector2.zero);
                colors.Add(hasColors ? sourceColors[vertexIndex] : Color.white);
            }

            int sourceSubMeshCount = sourceMesh.subMeshCount;
            if (sourceSubMeshCount <= 0)
            {
                continue;
            }

            while (subMeshTriangles.Count == 0)
            {
                subMeshTriangles.Add(new List<int>());
            }

            for (int subMeshIndex = 0; subMeshIndex < sourceSubMeshCount; subMeshIndex++)
            {
                int[] sourceTriangles = sourceMesh.GetTriangles(Mathf.Min(subMeshIndex, sourceMesh.subMeshCount - 1));
                AppendTriangles(sourceTriangles, vertexOffset, isMirrored, subMeshTriangles[0]);
            }
        }

        if (vertices.Count == 0 || subMeshTriangles.Count == 0)
        {
            throw new InvalidOperationException("결합할 수 있는 버텍스/삼각형이 없습니다.");
        }

        Mesh combinedMesh = new Mesh
        {
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };

        combinedMesh.SetVertices(vertices);
        combinedMesh.SetUVs(0, uv0);
        combinedMesh.SetUVs(1, uv1);
        if (anyColors)
        {
            combinedMesh.SetColors(colors);
        }

        combinedMesh.subMeshCount = subMeshTriangles.Count;
        for (int subMeshIndex = 0; subMeshIndex < subMeshTriangles.Count; subMeshIndex++)
        {
            combinedMesh.SetTriangles(subMeshTriangles[subMeshIndex], subMeshIndex, false);
        }

        if (recalculateNormals || !allNormalsValid)
        {
            combinedMesh.RecalculateNormals();
        }
        else
        {
            combinedMesh.SetNormals(normals);
        }

        if (recalculateTangents || !allTangentsValid)
        {
            combinedMesh.RecalculateTangents();
        }
        else
        {
            combinedMesh.SetTangents(tangents);
        }

        combinedMesh.RecalculateBounds();
        return combinedMesh;
    }

    private static void AppendTriangles(int[] sourceTriangles, int vertexOffset, bool reverseWinding, List<int> results)
    {
        if (sourceTriangles == null || results == null)
        {
            return;
        }

        for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
        {
            int a = sourceTriangles[i] + vertexOffset;
            int b = sourceTriangles[i + 1] + vertexOffset;
            int c = sourceTriangles[i + 2] + vertexOffset;
            if (reverseWinding)
            {
                results.Add(b);
                results.Add(a);
                results.Add(c);
            }
            else
            {
                results.Add(a);
                results.Add(b);
                results.Add(c);
            }
        }
    }

    private bool TryGetCombinedBounds(out Bounds bounds)
    {
        if (!combinedBoundsDirty)
        {
            bounds = cachedCombinedBounds;
            return cachedCombinedBoundsValid;
        }

        combinedBoundsDirty = false;
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        for (int entryIndex = 0; entryIndex < meshEntries.Count; entryIndex++)
        {
            MeshEntry entry = meshEntries[entryIndex];
            if (!IsValidEntry(entry))
            {
                continue;
            }

            Vector3[] sourceVertices = entry.mesh.vertices;
            if (sourceVertices == null || sourceVertices.Length == 0)
            {
                continue;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(entry.position, Quaternion.Euler(entry.rotationEuler), SanitizeScale(entry.scale));
            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                Vector3 transformedVertex = matrix.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                if (!hasBounds)
                {
                    bounds = new Bounds(transformedVertex, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(transformedVertex);
                }
            }
        }

        if (showPivotInPreview)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(pivotPosition, Vector3.one * 0.25f);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(pivotPosition);
            }
        }

        cachedCombinedBounds = bounds;
        cachedCombinedBoundsValid = hasBounds;
        return hasBounds;
    }

    private Bounds GetPreviewBounds()
    {
        if (TryGetCombinedBounds(out Bounds bounds))
        {
            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                bounds.Expand(0.5f);
            }

            return bounds;
        }

        return new Bounds(Vector3.zero, Vector3.one);
    }

    private void UpdatePreviewMeshIfNeeded()
    {
        if (!previewDirty)
        {
            return;
        }

        if (previewMesh != null)
        {
            DestroyImmediate(previewMesh);
            previewMesh = null;
        }

        if (CountValidSources() > 0)
        {
            try
            {
                previewMesh = BuildCombinedMesh(false);
                previewMesh.name = "MeshCombinePreview";
            }
            catch
            {
                previewMesh = null;
            }
        }

        previewDirty = false;
    }

    private int CountValidSources()
    {
        int count = 0;
        for (int i = 0; i < meshEntries.Count; i++)
        {
            if (IsValidEntry(meshEntries[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsValidEntry(MeshEntry entry)
    {
        return entry != null && entry.enabled && entry.mesh != null;
    }

    private void EnsurePreviewRenderer()
    {
        if (previewRenderer == null)
        {
            previewRenderer = new PreviewRenderUtility();
            previewRenderer.cameraFieldOfView = 30f;
            previewRenderer.lights[0].intensity = 1.15f;
            previewRenderer.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            previewRenderer.lights[1].intensity = 0.65f;
        }

        if (previewMaterial == null)
        {
            Shader shader = FindPreviewShader();
            if (shader == null)
            {
                return;
            }

            previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            ApplyMaterialColor(previewMaterial, PreviewMaterialColor);
        }
    }

    private static Shader FindPreviewShader()
    {
        for (int i = 0; i < PreviewShaderNames.Length; i++)
        {
            Shader shader = Shader.Find(PreviewShaderNames[i]);
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

        material.color = color;
    }

    private void DisposePreviewResources()
    {
        if (previewMesh != null)
        {
            DestroyImmediate(previewMesh);
            previewMesh = null;
        }

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

    private void MarkPreviewDirty()
    {
        previewDirty = true;
        combinedBoundsDirty = true;
        Repaint();
    }

    private static void DecomposeMatrix(Matrix4x4 matrix, out Vector3 position, out Vector3 rotationEuler, out Vector3 scale)
    {
        position = matrix.GetColumn(3);

        Vector3 right = matrix.GetColumn(0);
        Vector3 up = matrix.GetColumn(1);
        Vector3 forward = matrix.GetColumn(2);
        scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
        if (matrix.determinant < 0f)
        {
            scale.x = -scale.x;
            right = -right;
        }

        if (Mathf.Abs(scale.x) > 0.00001f)
        {
            right /= Mathf.Abs(scale.x);
        }

        if (Mathf.Abs(scale.y) > 0.00001f)
        {
            up /= Mathf.Abs(scale.y);
        }

        if (Mathf.Abs(scale.z) > 0.00001f)
        {
            forward /= Mathf.Abs(scale.z);
        }

        Quaternion rotation = Quaternion.identity;
        if (forward.sqrMagnitude > 0.00001f && up.sqrMagnitude > 0.00001f)
        {
            rotation = Quaternion.LookRotation(forward.normalized, up.normalized);
        }

        rotationEuler = rotation.eulerAngles;
        scale = SanitizeScale(scale);
    }

    private static Vector3 SanitizeScale(Vector3 value)
    {
        return new Vector3(
            Mathf.Approximately(value.x, 0f) ? 0.0001f : value.x,
            Mathf.Approximately(value.y, 0f) ? 0.0001f : value.y,
            Mathf.Approximately(value.z, 0f) ? 0.0001f : value.z);
    }

    private static string NormalizePathSeparators(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static string NormalizeToAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalizedPath = NormalizePathSeparators(path);
        if (normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, "Assets", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath;
        }

        return AbsolutePathToAssetPath(normalizedPath);
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        string normalizedAssetPath = NormalizePathSeparators(assetPath);
        if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedAssetPath, "Assets", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null)
        {
            return string.Empty;
        }

        return NormalizePathSeparators(Path.Combine(projectRoot.FullName, normalizedAssetPath));
    }

    private static void EnsureImportedMeshFolder()
    {
        if (!AssetDatabase.IsValidFolder(ImportedMeshFolder))
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(ImportedMeshFolder));
        }
    }

    private static string AbsolutePathToAssetPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return string.Empty;
        }

        absolutePath = NormalizePathSeparators(absolutePath);
        string dataPath = NormalizePathSeparators(Application.dataPath);
        if (!absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return "Assets" + absolutePath.Substring(dataPath.Length);
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName.Trim();
    }
}

internal static class HierarchyMeshMergeToChild
{
    private const string MenuPath = "GameObject/Merge To Child";
    private const string FallbackOutputFolder = "Assets/MergedMeshes";

    private struct MeshSource
    {
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Mesh mesh;
        public Matrix4x4 meshToRoot;
    }

    [MenuItem(MenuPath, false, 31)]
    private static void MergeToChild(MenuCommand command)
    {
        GameObject rootObject = command.context as GameObject;
        if (rootObject == null)
        {
            rootObject = Selection.activeGameObject;
        }

        if (rootObject == null)
        {
            EditorUtility.DisplayDialog("Merge To Child", "선택된 GameObject가 없습니다.", "OK");
            return;
        }

        MeshFilter rootFilter = rootObject.GetComponent<MeshFilter>();
        MeshRenderer rootRenderer = rootObject.GetComponent<MeshRenderer>();
        Mesh mainMesh = rootFilter != null ? rootFilter.sharedMesh : null;
        if (rootFilter == null || rootRenderer == null || mainMesh == null)
        {
            EditorUtility.DisplayDialog(
                "Merge To Child",
                "우클릭한 오브젝트에 MeshFilter, MeshRenderer, Mesh가 모두 있어야 합니다.",
                "OK");
            return;
        }

        string outputFolder = ResolveOutputFolder(rootObject, mainMesh);

        List<MeshSource> sources = CollectMeshSources(rootObject.transform);
        if (sources.Count <= 0)
        {
            EditorUtility.DisplayDialog("Merge To Child", "병합할 MeshRenderer/MeshFilter를 찾을 수 없습니다.", "OK");
            return;
        }

        Mesh combinedMesh;
        Material[] combinedMaterials;
        try
        {
            combinedMesh = BuildCombinedMesh(rootObject.name, sources, GetFirstMaterial(rootRenderer), out combinedMaterials);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Merge To Child Failed", exception.Message, "OK");
            return;
        }

        string safeName = MakeSafeFileName($"{rootObject.name}_Merged");
        string outputPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{safeName}.asset");
        combinedMesh.name = safeName;
        AssetDatabase.CreateAsset(combinedMesh, outputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Undo.RegisterCompleteObjectUndo(rootFilter, "Merge To Child");
        Undo.RegisterCompleteObjectUndo(rootRenderer, "Merge To Child");
        Undo.RegisterCompleteObjectUndo(rootObject.transform, "Merge To Child");
        rootFilter.sharedMesh = combinedMesh;
        rootRenderer.sharedMaterials = combinedMaterials;
        rootObject.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(rootFilter);
        EditorUtility.SetDirty(rootRenderer);
        EditorUtility.SetDirty(rootObject.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(rootFilter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(rootRenderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(rootObject.transform);

        for (int i = 0; i < sources.Count; i++)
        {
            MeshRenderer renderer = sources[i].renderer;
            if (renderer == null || renderer == rootRenderer)
            {
                continue;
            }

            Undo.RegisterCompleteObjectUndo(renderer, "Merge To Child");
            renderer.enabled = false;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        UnityEngine.Object createdAsset = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
        if (createdAsset != null)
        {
            EditorGUIUtility.PingObject(createdAsset);
        }

        Selection.activeGameObject = rootObject;
    }

    private static string ResolveOutputFolder(GameObject rootObject, Mesh mainMesh)
    {
        string mainMeshPath = NormalizePathSeparators(AssetDatabase.GetAssetPath(mainMesh));
        if (TryGetAssetFolder(mainMeshPath, out string meshFolder))
        {
            return meshFolder;
        }

        string prefabPath = NormalizePathSeparators(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rootObject));
        if (TryGetAssetFolder(prefabPath, out string prefabFolder))
        {
            return prefabFolder;
        }

        string objectPath = NormalizePathSeparators(AssetDatabase.GetAssetPath(rootObject));
        if (TryGetAssetFolder(objectPath, out string objectFolder))
        {
            return objectFolder;
        }

        EnsureAssetFolder(FallbackOutputFolder);
        return FallbackOutputFolder;
    }

    private static bool TryGetAssetFolder(string assetPath, out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(assetPath)
            || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (AssetDatabase.IsValidFolder(assetPath))
        {
            folder = assetPath;
            return true;
        }

        folder = NormalizePathSeparators(Path.GetDirectoryName(assetPath));
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = "Assets";
        }

        return folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        folderPath = NormalizePathSeparators(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath)
            || AssetDatabase.IsValidFolder(folderPath)
            || !folderPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string parent = NormalizePathSeparators(Path.GetDirectoryName(folderPath));
        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        EnsureAssetFolder(parent);
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }

    private static Material GetFirstMaterial(MeshRenderer renderer)
    {
        Material[] materials = renderer != null ? renderer.sharedMaterials : null;
        if (materials == null || materials.Length <= 0)
        {
            return null;
        }

        return materials[0];
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateMergeToChild()
    {
        GameObject rootObject = Selection.activeGameObject;
        if (rootObject == null)
        {
            return false;
        }

        MeshFilter rootFilter = rootObject.GetComponent<MeshFilter>();
        MeshRenderer rootRenderer = rootObject.GetComponent<MeshRenderer>();
        return rootFilter != null
               && rootFilter.sharedMesh != null
               && rootRenderer != null
               && rootObject.GetComponentsInChildren<MeshFilter>(true).Length > 0;
    }

    private static List<MeshSource> CollectMeshSources(Transform root)
    {
        List<MeshSource> sources = new List<MeshSource>();
        if (root == null)
        {
            return sources;
        }

        Matrix4x4 rootUnitScaleWorldToLocal = GetRootUnitScaleWorldToLocalMatrix(root);
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            MeshRenderer meshRenderer = meshFilter != null ? meshFilter.GetComponent<MeshRenderer>() : null;
            if (meshFilter == null || mesh == null || meshRenderer == null)
            {
                continue;
            }

            sources.Add(new MeshSource
            {
                filter = meshFilter,
                renderer = meshRenderer,
                mesh = mesh,
                meshToRoot = rootUnitScaleWorldToLocal * meshFilter.transform.localToWorldMatrix
            });
        }

        return sources;
    }

    private static Matrix4x4 GetRootUnitScaleWorldToLocalMatrix(Transform root)
    {
        if (root == null)
        {
            return Matrix4x4.identity;
        }

        Matrix4x4 parentLocalToWorld = root.parent != null
            ? root.parent.localToWorldMatrix
            : Matrix4x4.identity;
        Matrix4x4 rootUnitScaleLocalToWorld =
            parentLocalToWorld * Matrix4x4.TRS(root.localPosition, root.localRotation, Vector3.one);
        return rootUnitScaleLocalToWorld.inverse;
    }

    private static Mesh BuildCombinedMesh(
        string rootName,
        IReadOnlyList<MeshSource> sources,
        Material renderMaterial,
        out Material[] materials)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector4> tangents = new List<Vector4>();
        List<Vector2> uv0 = new List<Vector2>();
        List<Vector2> uv1 = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();

        bool allNormalsValid = true;
        bool allTangentsValid = true;
        bool anyColors = false;

        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            MeshSource source = sources[sourceIndex];
            Mesh sourceMesh = source.mesh;
            if (sourceMesh == null || sourceMesh.vertexCount <= 0 || sourceMesh.subMeshCount <= 0)
            {
                continue;
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] sourceNormals = sourceMesh.normals;
            Vector4[] sourceTangents = sourceMesh.tangents;
            Vector2[] sourceUv0 = sourceMesh.uv;
            Vector2[] sourceUv1 = sourceMesh.uv2;
            Color[] sourceColors = sourceMesh.colors;

            bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents != null && sourceTangents.Length == sourceVertices.Length;
            bool hasUv0 = sourceUv0 != null && sourceUv0.Length == sourceVertices.Length;
            bool hasUv1 = sourceUv1 != null && sourceUv1.Length == sourceVertices.Length;
            bool hasColors = sourceColors != null && sourceColors.Length == sourceVertices.Length;
            allNormalsValid &= hasNormals;
            allTangentsValid &= hasTangents;
            anyColors |= hasColors;

            Matrix4x4 matrix = source.meshToRoot;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            bool isMirrored = matrix.determinant < 0f;
            int vertexOffset = vertices.Count;

            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[vertexIndex]));
                normals.Add(hasNormals
                    ? normalMatrix.MultiplyVector(sourceNormals[vertexIndex]).normalized
                    : Vector3.up);

                if (hasTangents)
                {
                    Vector4 sourceTangent = sourceTangents[vertexIndex];
                    Vector3 transformedTangent = matrix.MultiplyVector(
                        new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z)).normalized;
                    tangents.Add(new Vector4(
                        transformedTangent.x,
                        transformedTangent.y,
                        transformedTangent.z,
                        isMirrored ? -sourceTangent.w : sourceTangent.w));
                }
                else
                {
                    tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                }

                uv0.Add(hasUv0 ? sourceUv0[vertexIndex] : Vector2.zero);
                uv1.Add(hasUv1 ? sourceUv1[vertexIndex] : Vector2.zero);
                colors.Add(hasColors ? sourceColors[vertexIndex] : Color.white);
            }

            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                int[] sourceTriangles = sourceMesh.GetTriangles(subMeshIndex);
                if (sourceTriangles == null || sourceTriangles.Length <= 0)
                {
                    continue;
                }

                AppendTriangles(sourceTriangles, vertexOffset, isMirrored, triangles);
            }
        }

        if (vertices.Count <= 0 || triangles.Count <= 0)
        {
            throw new InvalidOperationException("결합할 수 있는 버텍스/삼각형이 없습니다.");
        }

        Mesh combinedMesh = new Mesh
        {
            name = $"{rootName}_Merged",
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        combinedMesh.SetVertices(vertices);
        combinedMesh.SetUVs(0, uv0);
        combinedMesh.SetUVs(1, uv1);
        if (anyColors)
        {
            combinedMesh.SetColors(colors);
        }

        combinedMesh.subMeshCount = 1;
        combinedMesh.SetTriangles(triangles, 0, false);

        if (allNormalsValid)
        {
            combinedMesh.SetNormals(normals);
        }
        else
        {
            combinedMesh.RecalculateNormals();
        }

        if (allTangentsValid)
        {
            combinedMesh.SetTangents(tangents);
        }
        else
        {
            combinedMesh.RecalculateTangents();
        }

        combinedMesh.RecalculateBounds();
        materials = new[] { renderMaterial };
        return combinedMesh;
    }

    private static void AppendTriangles(int[] sourceTriangles, int vertexOffset, bool reverseWinding, List<int> results)
    {
        if (sourceTriangles == null || results == null)
        {
            return;
        }

        for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
        {
            int a = sourceTriangles[i] + vertexOffset;
            int b = sourceTriangles[i + 1] + vertexOffset;
            int c = sourceTriangles[i + 2] + vertexOffset;
            if (reverseWinding)
            {
                results.Add(b);
                results.Add(a);
                results.Add(c);
            }
            else
            {
                results.Add(a);
                results.Add(b);
                results.Add(c);
            }
        }
    }

    private static string NormalizePathSeparators(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static string MakeSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "MergedMesh";
        }

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        fileName = fileName.Trim();
        return string.IsNullOrEmpty(fileName) ? "MergedMesh" : fileName;
    }
}
