using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ProjectF.EditorTools.MeshSplit
{
    public sealed class MeshSplitEditorWindow : EditorWindow
    {
        private const float SidebarWidth = 340f;
        private const float MinPreviewHeight = 420f;
        private const float DefaultWeldTolerance = 0.0001f;
        private const float DefaultBrushRadius = 28f;
        private const float MinBrushRadius = 3f;
        private const float MaxBrushRadius = 180f;
        private const float MinPreviewZoom = 0.06f;
        private const float MaxPreviewZoom = 8f;
        private const float PreviewZoomWheelSensitivity = 0.08f;
        private const float MinPreviewDistanceScale = 0.2f;
        private const int BrushPreviewSegments = 48;
        private const int MaxHistorySteps = 32;
        private static readonly string[] PreviewShaderNames =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Standard",
            "Hidden/Internal-Colored"
        };

        [Serializable]
        private sealed class GroupDefinition
        {
            public string name;
            public Color color;
        }

        [SerializeField]
        private Object sourceAsset;
        [SerializeField]
        private float weldTolerance = DefaultWeldTolerance;
        [SerializeField]
        private List<GroupDefinition> groups = new List<GroupDefinition>();
        [SerializeField]
        private int activeGroupIndex;
        [SerializeField]
        private float brushRadius = DefaultBrushRadius;
        [SerializeField]
        private bool showWireframe = true;
        [SerializeField, ColorUsage(true, false)]
        private Color wireframeColor = new Color(0.025f, 0.025f, 0.025f, 0.78f);
        [SerializeField]
        private Vector2 previewOrbit = new Vector2(135f, -20f);
        [SerializeField]
        private float previewZoom = 1.35f;
        [SerializeField]
        private Vector2 previewPan;
        [SerializeField]
        private List<Vector3> visualGroupOffsets = new List<Vector3>();
        [SerializeField]
        private DefaultAsset outputFolderAsset;
        [SerializeField]
        private string outputFolderPath = "Assets";
        [SerializeField]
        private string outputName = "MeshSplit";

        private MeshSplitSourceData sourceData;
        private int[][] triangleAdjacency = Array.Empty<int[]>();
        private int[] triangleGroups = Array.Empty<int>();
        private int islandCount;
        private Mesh previewMesh;
        private Mesh wireframeMesh;
        private PreviewRenderUtility previewRenderer;
        private Material[] previewMaterials = Array.Empty<Material>();
        private Material wireframeMaterial;
        private bool previewMeshDirty = true;
        private bool previewMaterialsDirty = true;
        private bool wireframeMeshDirty = true;
        private bool wireframeMaterialDirty = true;
        private Vector2 scrollPosition;
        private string statusMessage = "FBX, OBJ, Prefab 또는 Mesh를 불러오세요.";
        private bool hasBrushPreview;
        private Vector2 brushPreviewPosition;
        private int[] brushVisitStamps = Array.Empty<int>();
        private int brushVisitStamp;
        private readonly Queue<int> brushQueue = new Queue<int>();
        private readonly List<int[]> undoHistory = new List<int[]>();
        private readonly List<int[]> redoHistory = new List<int[]>();
        private int[] paintStrokeBefore;
        private bool paintStrokeChanged;
        private int movingVisualGroupIndex = -1;
        private bool colorPickMode;

        [MenuItem("Window/ProjectF/Mesh Split")]
        [MenuItem("Tools/MapObject/Mesh Split")]
        public static void ShowWindow()
        {
            MeshSplitEditorWindow window = GetWindow<MeshSplitEditorWindow>("Mesh Split");
            window.minSize = new Vector2(860f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsurePreviewRenderer();
            if (sourceAsset != null)
            {
                EditorApplication.delayCall += ReloadSerializedSource;
            }
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= ReloadSerializedSource;
            DisposePreviewResources();
        }

        private void OnGUI()
        {
            HandleKeyboardShortcuts();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSidebar();
                DrawPreviewPanel();
            }
        }

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(SidebarWidth)))
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                DrawSourceSection();
                EditorGUILayout.Space(8f);
                DrawGroupSection();
                EditorGUILayout.Space(8f);
                DrawBrushSection();
                EditorGUILayout.Space(8f);
                DrawOutputSection();
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            Object nextSource = EditorGUILayout.ObjectField("FBX / OBJ / Mesh", sourceAsset, typeof(Object), false);
            if (nextSource != sourceAsset)
            {
                LoadSource(nextSource);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                {
                    LoadSource(Selection.activeObject);
                }

                using (new EditorGUI.DisabledScope(sourceAsset == null))
                {
                    if (GUILayout.Button("Reanalyze"))
                    {
                        LoadSource(sourceAsset);
                    }
                }
            }

            weldTolerance = Mathf.Max(0.0000001f, EditorGUILayout.FloatField("Weld Tolerance", weldTolerance));
            EditorGUILayout.HelpBox(
                "같은 MeshFilter 안에서 위치가 같은 버텍스를 연결된 것으로 판단합니다. UV/하드 엣지로 중복된 버텍스도 같은 아일랜드로 묶입니다.",
                MessageType.None);

            if (sourceData != null)
            {
                EditorGUILayout.LabelField("Vertices", sourceData.Vertices.Length.ToString());
                EditorGUILayout.LabelField("Triangles", sourceData.TriangleCount.ToString());
                EditorGUILayout.LabelField("Disconnected Islands", islandCount.ToString());
            }
        }

        private void DrawGroupSection()
        {
            EditorGUILayout.LabelField("Color Groups", EditorStyles.boldLabel);
            if (sourceData == null || groups.Count == 0)
            {
                EditorGUILayout.HelpBox("소스를 분석하면 아일랜드별 색 그룹이 생성됩니다.", MessageType.Info);
                return;
            }

            activeGroupIndex = Mathf.Clamp(activeGroupIndex, 0, groups.Count - 1);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                GroupDefinition group = groups[groupIndex];
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previousBackground = GUI.backgroundColor;
                    GUI.backgroundColor = group.color;
                    bool selected = GUILayout.Toggle(
                        activeGroupIndex == groupIndex,
                        group.name,
                        "Button",
                        GUILayout.MinWidth(118f));
                    GUI.backgroundColor = previousBackground;
                    if (selected)
                    {
                        activeGroupIndex = groupIndex;
                    }

                    EditorGUI.BeginChangeCheck();
                    Color nextColor = EditorGUILayout.ColorField(GUIContent.none, group.color, false, false, false, GUILayout.Width(52f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        nextColor.a = 1f;
                        group.color = nextColor;
                        previewMaterialsDirty = true;
                        EditorUtility.SetDirty(this);
                        Repaint();
                    }
                }
            }

            if (GUILayout.Button("Add Color Group"))
            {
                EnsureVisualGroupOffsetCount();
                int newIndex = groups.Count;
                groups.Add(new GroupDefinition
                {
                    name = $"Group {newIndex + 1:00}",
                    color = GenerateGroupColor(newIndex)
                });
                visualGroupOffsets.Add(Vector3.zero);
                activeGroupIndex = newIndex;
                previewMeshDirty = true;
                wireframeMeshDirty = true;
                previewMaterialsDirty = true;
                EditorUtility.SetDirty(this);
            }

            EditorGUILayout.HelpBox(
                "저장 시 색 값이 완전히 같은 그룹들은 하나의 Mesh로 합쳐집니다.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(undoHistory.Count == 0))
                {
                    if (GUILayout.Button("Undo"))
                    {
                        UndoPaint();
                    }
                }

                using (new EditorGUI.DisabledScope(redoHistory.Count == 0))
                {
                    if (GUILayout.Button("Redo"))
                    {
                        RedoPaint();
                    }
                }
            }
        }

        private void DrawBrushSection()
        {
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(sourceData == null || groups.Count == 0))
            {
                GUIContent pickColorContent = new GUIContent(
                    "Pick Color",
                    "Click a vertex or face in the preview to select its color group.");
                bool nextColorPickMode = GUILayout.Toggle(colorPickMode, pickColorContent, "Button");
                if (nextColorPickMode != colorPickMode)
                {
                    colorPickMode = nextColorPickMode;
                    hasBrushPreview = false;
                    statusMessage = colorPickMode
                        ? "미리보기의 버텍스나 면을 클릭하면 해당 색 그룹을 선택합니다."
                        : "색 선택 모드를 해제했습니다.";
                    Repaint();
                }
            }

            brushRadius = EditorGUILayout.Slider("Radius (px)", brushRadius, MinBrushRadius, MaxBrushRadius);
            EditorGUI.BeginChangeCheck();
            bool nextShowWireframe = EditorGUILayout.Toggle("Show Wireframe", showWireframe);
            Color nextWireframeColor = EditorGUILayout.ColorField("Wireframe Color", wireframeColor);
            if (EditorGUI.EndChangeCheck())
            {
                showWireframe = nextShowWireframe;
                wireframeColor = nextWireframeColor;
                wireframeMaterialDirty = true;
                EditorUtility.SetDirty(this);
                Repaint();
            }

            using (new EditorGUI.DisabledScope(!HasVisualGroupOffsets()))
            {
                if (GUILayout.Button("Reset Visual Positions"))
                {
                    ResetVisualGroupOffsets();
                }
            }

            EditorGUILayout.HelpBox(
                "Pick Color → 좌클릭: 클릭한 버텍스/면의 색 선택\n좌클릭/드래그: 선택 색 칠하기\nShift+좌클릭: 연결된 아일랜드 전체 칠하기\nCtrl+좌클릭 드래그: 클릭한 그룹을 시각적으로만 이동\n우클릭 드래그: 회전 · 휠클릭 드래그: 이동 · 휠: 확대/축소",
                MessageType.None);
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            DefaultAsset nextFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder",
                outputFolderAsset,
                typeof(DefaultAsset),
                false);
            if (nextFolder != outputFolderAsset)
            {
                string path = nextFolder != null ? AssetDatabase.GetAssetPath(nextFolder) : string.Empty;
                if (nextFolder == null || AssetDatabase.IsValidFolder(path))
                {
                    outputFolderAsset = nextFolder;
                    outputFolderPath = nextFolder != null ? path : "Assets";
                }
            }

            outputName = EditorGUILayout.TextField("Name", outputName);
            using (new EditorGUI.DisabledScope(sourceData == null || triangleGroups.Length == 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save FBX", GUILayout.Height(32f)))
                    {
                        SaveSplitModel(MeshSplitExportFormat.Fbx, false);
                    }

                    if (GUILayout.Button("Save OBJ", GUILayout.Height(32f)))
                    {
                        SaveSplitModel(MeshSplitExportFormat.Obj, false);
                    }

                    bool canOverwrite = TryGetOverwriteTarget(out string overwritePath, out _);
                    using (new EditorGUI.DisabledScope(!canOverwrite))
                    {
                        GUIContent overwriteContent = new GUIContent(
                            "Overwrite",
                            canOverwrite ? $"Overwrite {overwritePath}" : "Available only for FBX or OBJ sources under Assets.");
                        if (GUILayout.Button(overwriteContent, GUILayout.Height(32f)))
                        {
                            SaveSplitModel(default, true);
                        }
                    }
                }
            }
        }

        private void DrawPreviewPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                Rect previewRect = GUILayoutUtility.GetRect(
                    1f,
                    MinPreviewHeight,
                    GUILayout.ExpandWidth(true),
                    GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.09f, 1f));
                EnsurePreviewRenderer();
                UpdatePreviewResourcesIfNeeded();
                SetupPreviewCamera(previewRect);
                HandlePreviewInput(previewRect);

                if (Event.current.type == EventType.Repaint)
                {
                    RenderPreview(previewRect);
                    DrawBrushPreviewOverlay(previewRect);
                }

                if (sourceData == null)
                {
                    EditorGUI.DropShadowLabel(previewRect, "Load FBX / OBJ / Prefab / Mesh");
                }
            }
        }

        private void LoadSource(Object candidate)
        {
            if (candidate != null && candidate is not GameObject && candidate is not Mesh)
            {
                statusMessage = "FBX, OBJ, Prefab 또는 Mesh Asset만 지원합니다.";
                return;
            }

            sourceAsset = candidate;
            sourceData = null;
            triangleAdjacency = Array.Empty<int[]>();
            triangleGroups = Array.Empty<int>();
            islandCount = 0;
            groups.Clear();
            visualGroupOffsets.Clear();
            activeGroupIndex = 0;
            movingVisualGroupIndex = -1;
            colorPickMode = false;
            ClearHistory();
            DestroyPreviewMesh();
            DestroyWireframeMesh();
            DestroyPreviewMaterials();
            previewMeshDirty = false;
            wireframeMeshDirty = false;
            previewMaterialsDirty = false;
            if (candidate == null)
            {
                statusMessage = "FBX, OBJ, Prefab 또는 Mesh를 불러오세요.";
                Repaint();
                return;
            }

            if (!MeshSplitUtility.TryBuildSourceData(candidate, out sourceData, out string error))
            {
                statusMessage = error;
                Repaint();
                return;
            }

            triangleAdjacency = MeshSplitUtility.BuildTriangleAdjacency(sourceData, weldTolerance);
            triangleGroups = MeshSplitUtility.BuildConnectedComponentGroups(triangleAdjacency, out islandCount);
            brushVisitStamps = new int[sourceData.TriangleCount];
            for (int groupIndex = 0; groupIndex < islandCount; groupIndex++)
            {
                Color groupColor = MeshSplitUtility.TryGetImportedGroupColor(
                    sourceData,
                    triangleGroups,
                    groupIndex,
                    out Color importedColor)
                    ? importedColor
                    : GenerateGroupColor(groupIndex);
                groups.Add(new GroupDefinition
                {
                    name = $"Group {groupIndex + 1:00}",
                    color = groupColor
                });
            }

            if (groups.Count == 0)
            {
                groups.Add(new GroupDefinition { name = "Group 01", color = GenerateGroupColor(0) });
            }

            EnsureVisualGroupOffsetCount();

            outputName = MeshSplitUtility.MakeSafeFileName($"{sourceData.SourceName}_Split");
            TryUseSourceFolder(candidate);
            previewOrbit = new Vector2(135f, -20f);
            previewZoom = 1.35f;
            previewPan = Vector2.zero;
            previewMeshDirty = true;
            wireframeMeshDirty = true;
            previewMaterialsDirty = true;
            statusMessage = $"{islandCount}개 아일랜드를 분석했습니다. 브러시로 색 그룹을 편집할 수 있습니다.";
            EditorUtility.SetDirty(this);
            Repaint();
        }

        private void ReloadSerializedSource()
        {
            if (this != null && sourceAsset != null && sourceData == null)
            {
                LoadSource(sourceAsset);
            }
        }

        private void TryUseSourceFolder(Object source)
        {
            string assetPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            outputFolderPath = folder;
            outputFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
        }

        private void EnsurePreviewRenderer()
        {
            if (previewRenderer != null)
            {
                return;
            }

            previewRenderer = new PreviewRenderUtility();
            previewRenderer.camera.clearFlags = CameraClearFlags.Color;
            previewRenderer.camera.backgroundColor = new Color(0.08f, 0.08f, 0.09f, 1f);
            previewRenderer.cameraFieldOfView = 30f;
            previewRenderer.lights[0].intensity = 1.15f;
            previewRenderer.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            previewRenderer.lights[1].intensity = 0.7f;
            previewRenderer.ambientColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        }

        private void UpdatePreviewResourcesIfNeeded()
        {
            EnsureVisualGroupOffsetCount();
            if (previewMeshDirty)
            {
                DestroyPreviewMesh();
                if (sourceData != null && groups.Count > 0)
                {
                    previewMesh = MeshSplitUtility.BuildPreviewMesh(sourceData, triangleGroups, groups.Count);
                }

                previewMeshDirty = false;
            }

            if (wireframeMeshDirty)
            {
                DestroyWireframeMesh();
                if (sourceData != null)
                {
                    wireframeMesh = MeshSplitUtility.BuildWireframeMesh(
                        sourceData,
                        triangleGroups,
                        groups.Count,
                        weldTolerance);
                }

                wireframeMeshDirty = false;
            }

            if (previewMaterialsDirty)
            {
                DestroyPreviewMaterials();
                Shader shader = FindPreviewShader();
                if (shader != null)
                {
                    previewMaterials = new Material[groups.Count];
                    for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                    {
                        Material material = new Material(shader)
                        {
                            name = $"MeshSplitPreview_Group_{groupIndex + 1}",
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        SetMaterialColor(material, groups[groupIndex].color);
                        previewMaterials[groupIndex] = material;
                    }
                }

                previewMaterialsDirty = false;
            }

            if (wireframeMaterialDirty || wireframeMaterial == null)
            {
                DestroyWireframeMaterial();
                wireframeMaterial = CreateWireframeMaterial(wireframeColor);
                wireframeMaterialDirty = false;
            }
        }

        private bool TryBeginVisualGroupMove(Rect previewRect, Vector2 mousePosition)
        {
            movingVisualGroupIndex = -1;
            hasBrushPreview = false;
            if (sourceData == null
                || groups.Count == 0
                || !TryBuildPreviewRay(previewRect, mousePosition, out Ray ray)
                || !TryRaycastSource(ray, out int hitTriangleIndex))
            {
                return false;
            }

            EnsureVisualGroupOffsetCount();
            movingVisualGroupIndex = Mathf.Clamp(triangleGroups[hitTriangleIndex], 0, groups.Count - 1);
            statusMessage = $"{groups[movingVisualGroupIndex].name}의 미리보기 위치를 이동합니다. 저장 결과에는 반영되지 않습니다.";
            return true;
        }

        private void MoveVisualGroup(Rect previewRect, Vector2 screenDelta)
        {
            if (movingVisualGroupIndex < 0
                || movingVisualGroupIndex >= groups.Count
                || sourceData == null
                || previewRenderer == null
                || previewRenderer.camera == null
                || previewRect.height <= 0f)
            {
                return;
            }

            EnsureVisualGroupOffsetCount();
            Camera camera = previewRenderer.camera;
            Vector3 currentOffset = visualGroupOffsets[movingVisualGroupIndex];
            Vector3 referencePoint = sourceData.Bounds.center + currentOffset;
            float depth = Vector3.Dot(referencePoint - camera.transform.position, camera.transform.forward);
            depth = Mathf.Max(depth, camera.nearClipPlane * 2f);
            float verticalWorldSize = 2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float unitsPerPixel = verticalWorldSize / previewRect.height;
            Vector3 worldDelta = camera.transform.right * (screenDelta.x * unitsPerPixel)
                + camera.transform.up * (-screenDelta.y * unitsPerPixel);
            visualGroupOffsets[movingVisualGroupIndex] = currentOffset + worldDelta;
            statusMessage = $"{groups[movingVisualGroupIndex].name}의 미리보기 위치만 이동했습니다.";
            EditorUtility.SetDirty(this);
        }

        private void EnsureVisualGroupOffsetCount()
        {
            while (visualGroupOffsets.Count < groups.Count)
            {
                visualGroupOffsets.Add(Vector3.zero);
            }

            if (visualGroupOffsets.Count > groups.Count)
            {
                visualGroupOffsets.RemoveRange(groups.Count, visualGroupOffsets.Count - groups.Count);
            }
        }

        private Vector3 GetVisualGroupOffset(int groupIndex)
        {
            return groupIndex >= 0 && groupIndex < visualGroupOffsets.Count
                ? visualGroupOffsets[groupIndex]
                : Vector3.zero;
        }

        private Vector3 GetTriangleVisualOffset(int triangleIndex)
        {
            if (triangleIndex < 0 || triangleIndex >= triangleGroups.Length)
            {
                return Vector3.zero;
            }

            return GetVisualGroupOffset(triangleGroups[triangleIndex]);
        }

        private bool HasVisualGroupOffsets()
        {
            EnsureVisualGroupOffsetCount();
            for (int groupIndex = 0; groupIndex < visualGroupOffsets.Count; groupIndex++)
            {
                if (visualGroupOffsets[groupIndex].sqrMagnitude > 0.0000000001f)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetVisualGroupOffsets()
        {
            EnsureVisualGroupOffsetCount();
            for (int groupIndex = 0; groupIndex < visualGroupOffsets.Count; groupIndex++)
            {
                visualGroupOffsets[groupIndex] = Vector3.zero;
            }

            movingVisualGroupIndex = -1;
            statusMessage = "그룹 미리보기 위치를 원래 자리로 되돌렸습니다.";
            EditorUtility.SetDirty(this);
            Repaint();
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

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static Material CreateWireframeMaterial(Color color)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null || !shader.isSupported)
            {
                shader = FindPreviewShader();
            }

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = "MeshSplitPreview_Wireframe",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent
            };
            SetMaterialColor(material, color);
            SetMaterialIntIfPresent(material, "_SrcBlend", (int)BlendMode.SrcAlpha);
            SetMaterialIntIfPresent(material, "_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            SetMaterialIntIfPresent(material, "_Cull", (int)CullMode.Off);
            SetMaterialIntIfPresent(material, "_ZWrite", 0);
            SetMaterialIntIfPresent(material, "_ZTest", (int)CompareFunction.LessEqual);
            return material;
        }

        private static void SetMaterialIntIfPresent(Material material, string propertyName, int value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetInt(propertyName, value);
            }
        }

        private void SetupPreviewCamera(Rect previewRect)
        {
            if (previewRenderer == null)
            {
                return;
            }

            Bounds bounds = sourceData != null ? sourceData.Bounds : new Bounds(Vector3.zero, Vector3.one);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
            float distance = radius * Mathf.Max(MinPreviewDistanceScale, previewZoom * 2.4f);
            Quaternion orbitRotation = Quaternion.Euler(previewOrbit.y, previewOrbit.x, 0f);
            Camera camera = previewRenderer.camera;
            Vector3 panOffset = orbitRotation * new Vector3(previewPan.x, previewPan.y, 0f);
            Vector3 center = bounds.center + panOffset;
            camera.transform.position = center + orbitRotation * (Vector3.back * distance);
            camera.transform.LookAt(center);
            camera.fieldOfView = 30f;
            camera.aspect = previewRect.height > 0f ? previewRect.width / previewRect.height : 1f;
            camera.nearClipPlane = Mathf.Max(0.001f, distance * 0.005f);
            camera.farClipPlane = Mathf.Max(100f, distance + radius * 8f);
        }

        private void RenderPreview(Rect previewRect)
        {
            if (previewRenderer == null)
            {
                return;
            }

            previewRenderer.BeginPreview(previewRect, GUIStyle.none);
            if (previewMesh != null && previewMaterials.Length > 0)
            {
                int renderPassCount = Mathf.Min(previewMesh.subMeshCount, previewMaterials.Length);
                for (int subMeshIndex = 0; subMeshIndex < renderPassCount; subMeshIndex++)
                {
                    Matrix4x4 groupMatrix = Matrix4x4.Translate(GetVisualGroupOffset(subMeshIndex));
                    previewRenderer.DrawMesh(previewMesh, groupMatrix, previewMaterials[subMeshIndex], subMeshIndex);
                }
            }

            if (showWireframe && wireframeMesh != null && wireframeMaterial != null)
            {
                int wireframePassCount = Mathf.Min(wireframeMesh.subMeshCount, groups.Count);
                for (int subMeshIndex = 0; subMeshIndex < wireframePassCount; subMeshIndex++)
                {
                    Matrix4x4 groupMatrix = Matrix4x4.Translate(GetVisualGroupOffset(subMeshIndex));
                    previewRenderer.DrawMesh(wireframeMesh, groupMatrix, wireframeMaterial, subMeshIndex);
                }
            }

            previewRenderer.camera.Render();
            Texture previewTexture = previewRenderer.EndPreview();
            GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, false);
        }

        private void HandlePreviewInput(Rect previewRect)
        {
            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            int controlId = GUIUtility.GetControlID("MeshSplitPreview".GetHashCode(), FocusType.Passive, previewRect);
            bool inside = previewRect.Contains(current.mousePosition);
            if (inside && colorPickMode)
            {
                EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.ArrowPlus);
            }
            else if (inside && current.control)
            {
                EditorGUIUtility.AddCursorRect(previewRect, MouseCursor.MoveArrow);
            }

            if (inside && current.type == EventType.MouseMove)
            {
                UpdateBrushPreview(previewRect, current.mousePosition);
                Repaint();
            }
            else if (!inside && GUIUtility.hotControl != controlId)
            {
                hasBrushPreview = false;
            }

            if (inside && current.type == EventType.ScrollWheel)
            {
                previewZoom = Mathf.Clamp(
                    previewZoom * Mathf.Exp(current.delta.y * PreviewZoomWheelSensitivity),
                    MinPreviewZoom,
                    MaxPreviewZoom);
                current.Use();
                Repaint();
                return;
            }

            if (inside && current.type == EventType.MouseDown)
            {
                if (current.button == 0 && colorPickMode)
                {
                    TryPickColorAtMousePosition(previewRect, current.mousePosition);
                    current.Use();
                    Repaint();
                    return;
                }

                GUIUtility.hotControl = controlId;
                if (current.button == 0)
                {
                    if (current.control)
                    {
                        TryBeginVisualGroupMove(previewRect, current.mousePosition);
                    }
                    else
                    {
                        movingVisualGroupIndex = -1;
                        BeginPaintStroke();
                        PaintAtMousePosition(previewRect, current.mousePosition, current.shift);
                    }
                }

                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
            {
                if (current.button == 0)
                {
                    if (movingVisualGroupIndex >= 0)
                    {
                        MoveVisualGroup(previewRect, current.delta);
                    }
                    else if (paintStrokeBefore != null)
                    {
                        PaintAtMousePosition(previewRect, current.mousePosition, current.shift);
                    }
                }
                else if (current.button == 1)
                {
                    previewOrbit.x += current.delta.x * 0.5f;
                    previewOrbit.y = Mathf.Clamp(previewOrbit.y - current.delta.y * 0.5f, -89f, 89f);
                    SetupPreviewCamera(previewRect);
                    UpdateBrushPreview(previewRect, current.mousePosition);
                }
                else if (current.button == 2)
                {
                    float panScale = sourceData != null
                        ? Mathf.Max(0.0001f, sourceData.Bounds.extents.magnitude * 0.0025f)
                        : 0.0025f;
                    previewPan.x -= current.delta.x * panScale;
                    previewPan.y += current.delta.y * panScale;
                    SetupPreviewCamera(previewRect);
                    UpdateBrushPreview(previewRect, current.mousePosition);
                }

                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                if (current.button == 0)
                {
                    if (paintStrokeBefore != null)
                    {
                        FinishPaintStroke();
                    }

                    movingVisualGroupIndex = -1;
                }

                GUIUtility.hotControl = 0;
                current.Use();
                Repaint();
            }
        }

        private void UpdateBrushPreview(Rect previewRect, Vector2 mousePosition)
        {
            hasBrushPreview = false;
            if (colorPickMode || (Event.current != null && Event.current.control))
            {
                return;
            }

            if (sourceData == null || groups.Count == 0 || !TryBuildPreviewRay(previewRect, mousePosition, out Ray ray))
            {
                return;
            }

            if (!TryRaycastSource(ray, out _))
            {
                return;
            }

            brushPreviewPosition = mousePosition;
            hasBrushPreview = true;
        }

        private bool TryPickColorAtMousePosition(Rect previewRect, Vector2 mousePosition)
        {
            if (sourceData == null
                || groups.Count == 0
                || !TryBuildPreviewRay(previewRect, mousePosition, out Ray ray)
                || !TryRaycastSource(ray, out int hitTriangleIndex)
                || hitTriangleIndex < 0
                || hitTriangleIndex >= triangleGroups.Length)
            {
                statusMessage = "색을 선택할 메쉬 버텍스나 면을 찾지 못했습니다.";
                return false;
            }

            int pickedGroupIndex = triangleGroups[hitTriangleIndex];
            if (pickedGroupIndex < 0 || pickedGroupIndex >= groups.Count)
            {
                statusMessage = "클릭한 메쉬에 유효한 색 그룹이 없습니다.";
                return false;
            }

            activeGroupIndex = pickedGroupIndex;
            colorPickMode = false;
            hasBrushPreview = false;
            statusMessage = $"{groups[pickedGroupIndex].name} 색을 선택했습니다.";
            EditorUtility.SetDirty(this);
            return true;
        }

        private void PaintAtMousePosition(Rect previewRect, Vector2 mousePosition, bool fillIsland)
        {
            if (sourceData == null || groups.Count == 0 || !TryBuildPreviewRay(previewRect, mousePosition, out Ray ray))
            {
                return;
            }

            if (!TryRaycastSource(ray, out int hitTriangleIndex))
            {
                hasBrushPreview = false;
                return;
            }

            brushPreviewPosition = mousePosition;
            hasBrushPreview = true;
            bool changed = fillIsland
                ? PaintConnectedIsland(hitTriangleIndex)
                : PaintBrush(previewRect, mousePosition, hitTriangleIndex);
            if (!changed)
            {
                return;
            }

            paintStrokeChanged = true;
            previewMeshDirty = true;
            wireframeMeshDirty = true;
            statusMessage = fillIsland ? "연결된 아일랜드의 색 그룹을 변경했습니다." : "브러시로 색 그룹을 변경했습니다.";
        }

        private bool PaintConnectedIsland(int startTriangle)
        {
            BeginBrushVisit();
            brushQueue.Enqueue(startTriangle);
            bool changed = false;
            while (brushQueue.Count > 0)
            {
                int triangleIndex = brushQueue.Dequeue();
                if (!TryVisitTriangle(triangleIndex))
                {
                    continue;
                }

                if (triangleGroups[triangleIndex] != activeGroupIndex)
                {
                    triangleGroups[triangleIndex] = activeGroupIndex;
                    changed = true;
                }

                int[] neighbors = triangleAdjacency[triangleIndex];
                for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
                {
                    brushQueue.Enqueue(neighbors[neighborIndex]);
                }
            }

            return changed;
        }

        private bool PaintBrush(Rect previewRect, Vector2 brushCenter, int startTriangle)
        {
            BeginBrushVisit();
            brushQueue.Enqueue(startTriangle);
            bool changed = false;
            while (brushQueue.Count > 0)
            {
                int triangleIndex = brushQueue.Dequeue();
                if (!TryVisitTriangle(triangleIndex))
                {
                    continue;
                }

                bool insideBrush = triangleIndex == startTriangle
                    || IsTriangleInsideScreenBrush(previewRect, triangleIndex, brushCenter, brushRadius);
                if (!insideBrush)
                {
                    continue;
                }

                if (triangleGroups[triangleIndex] != activeGroupIndex)
                {
                    triangleGroups[triangleIndex] = activeGroupIndex;
                    changed = true;
                }

                int[] neighbors = triangleAdjacency[triangleIndex];
                for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
                {
                    brushQueue.Enqueue(neighbors[neighborIndex]);
                }
            }

            return changed;
        }

        private void BeginBrushVisit()
        {
            brushQueue.Clear();
            brushVisitStamp++;
            if (brushVisitStamp == int.MaxValue)
            {
                Array.Clear(brushVisitStamps, 0, brushVisitStamps.Length);
                brushVisitStamp = 1;
            }
        }

        private bool TryVisitTriangle(int triangleIndex)
        {
            if (triangleIndex < 0 || triangleIndex >= brushVisitStamps.Length
                || brushVisitStamps[triangleIndex] == brushVisitStamp)
            {
                return false;
            }

            brushVisitStamps[triangleIndex] = brushVisitStamp;
            return true;
        }

        private bool IsTriangleInsideScreenBrush(Rect previewRect, int triangleIndex, Vector2 center, float radius)
        {
            int baseIndex = triangleIndex * 3;
            int[] triangles = sourceData.Triangles;
            Vector3 visualOffset = GetTriangleVisualOffset(triangleIndex);
            if (!TryWorldToPreviewGuiPoint(previewRect, sourceData.Vertices[triangles[baseIndex]] + visualOffset, out Vector2 p0)
                || !TryWorldToPreviewGuiPoint(previewRect, sourceData.Vertices[triangles[baseIndex + 1]] + visualOffset, out Vector2 p1)
                || !TryWorldToPreviewGuiPoint(previewRect, sourceData.Vertices[triangles[baseIndex + 2]] + visualOffset, out Vector2 p2))
            {
                return false;
            }

            return PointInTriangle(center, p0, p1, p2)
                || DistanceToSegment(center, p0, p1) <= radius
                || DistanceToSegment(center, p1, p2) <= radius
                || DistanceToSegment(center, p2, p0) <= radius;
        }

        private bool TryRaycastSource(Ray ray, out int hitTriangleIndex)
        {
            hitTriangleIndex = -1;
            if (sourceData == null)
            {
                return false;
            }

            float closestDistance = float.MaxValue;
            int closestTriangle = -1;
            int[] triangles = sourceData.Triangles;
            Vector3[] vertices = sourceData.Vertices;
            for (int triangleIndex = 0; triangleIndex < sourceData.TriangleCount; triangleIndex++)
            {
                int baseIndex = triangleIndex * 3;
                Vector3 visualOffset = GetTriangleVisualOffset(triangleIndex);
                if (!IntersectRayTriangle(
                        ray,
                        vertices[triangles[baseIndex]] + visualOffset,
                        vertices[triangles[baseIndex + 1]] + visualOffset,
                        vertices[triangles[baseIndex + 2]] + visualOffset,
                        out float distance)
                    || distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                closestTriangle = triangleIndex;
            }

            if (closestTriangle < 0)
            {
                return false;
            }

            hitTriangleIndex = closestTriangle;
            return true;
        }

        private bool TryBuildPreviewRay(Rect previewRect, Vector2 mousePosition, out Ray ray)
        {
            ray = default;
            if (previewRenderer == null || previewRect.width <= 0f || previewRect.height <= 0f)
            {
                return false;
            }

            Vector2 localPosition = mousePosition - previewRect.position;
            ray = previewRenderer.camera.ViewportPointToRay(new Vector3(
                Mathf.Clamp01(localPosition.x / previewRect.width),
                Mathf.Clamp01(1f - localPosition.y / previewRect.height),
                0f));
            return true;
        }

        private bool TryWorldToPreviewGuiPoint(Rect previewRect, Vector3 worldPoint, out Vector2 guiPoint)
        {
            guiPoint = Vector2.zero;
            if (previewRenderer == null || previewRenderer.camera == null)
            {
                return false;
            }

            Vector3 viewportPoint = previewRenderer.camera.WorldToViewportPoint(worldPoint);
            if (viewportPoint.z <= 0f)
            {
                return false;
            }

            guiPoint = new Vector2(
                previewRect.x + viewportPoint.x * previewRect.width,
                previewRect.y + (1f - viewportPoint.y) * previewRect.height);
            return true;
        }

        private void DrawBrushPreviewOverlay(Rect previewRect)
        {
            if (!hasBrushPreview || groups.Count == 0)
            {
                return;
            }

            Color color = groups[Mathf.Clamp(activeGroupIndex, 0, groups.Count - 1)].color;
            Color fillColor = color;
            fillColor.a = 0.14f;
            Color outlineColor = color;
            outlineColor.a = 0.95f;
            Vector3[] circle = BuildScreenSpaceCircle(brushPreviewPosition, brushRadius);
            Vector3[] closedCircle = new Vector3[circle.Length + 1];
            Array.Copy(circle, closedCircle, circle.Length);
            closedCircle[closedCircle.Length - 1] = circle[0];

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = fillColor;
            Handles.DrawAAConvexPolygon(circle);
            Handles.color = outlineColor;
            Handles.DrawAAPolyLine(2.5f, closedCircle);
            Handles.color = previousColor;
            Handles.EndGUI();
        }

        private static Vector3[] BuildScreenSpaceCircle(Vector2 center, float radius)
        {
            Vector3[] points = new Vector3[BrushPreviewSegments];
            for (int pointIndex = 0; pointIndex < BrushPreviewSegments; pointIndex++)
            {
                float angle = Mathf.PI * 2f * pointIndex / BrushPreviewSegments;
                points[pointIndex] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            return points;
        }

        private static bool IntersectRayTriangle(Ray ray, Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, out float distance)
        {
            const float epsilon = 0.000001f;
            distance = 0f;
            Vector3 edge1 = vertex1 - vertex0;
            Vector3 edge2 = vertex2 - vertex0;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < epsilon)
            {
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 t = ray.origin - vertex0;
            float u = Vector3.Dot(t, p) * inverseDeterminant;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverseDeterminant;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            distance = Vector3.Dot(edge2, q) * inverseDeterminant;
            return distance > epsilon;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(point - b, a - b);
            float d2 = Cross(point - c, b - c);
            float d3 = Cross(point - a, c - a);
            bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private void BeginPaintStroke()
        {
            paintStrokeBefore = triangleGroups.Length > 0 ? (int[])triangleGroups.Clone() : null;
            paintStrokeChanged = false;
        }

        private void FinishPaintStroke()
        {
            if (paintStrokeChanged && paintStrokeBefore != null)
            {
                PushHistory(undoHistory, paintStrokeBefore);
                redoHistory.Clear();
                EditorUtility.SetDirty(this);
            }

            paintStrokeBefore = null;
            paintStrokeChanged = false;
        }

        private void UndoPaint()
        {
            if (undoHistory.Count == 0 || triangleGroups.Length == 0)
            {
                return;
            }

            PushHistory(redoHistory, (int[])triangleGroups.Clone());
            triangleGroups = PopHistory(undoHistory);
            previewMeshDirty = true;
            wireframeMeshDirty = true;
            statusMessage = "색 그룹 편집을 되돌렸습니다.";
            Repaint();
        }

        private void RedoPaint()
        {
            if (redoHistory.Count == 0 || triangleGroups.Length == 0)
            {
                return;
            }

            PushHistory(undoHistory, (int[])triangleGroups.Clone());
            triangleGroups = PopHistory(redoHistory);
            previewMeshDirty = true;
            wireframeMeshDirty = true;
            statusMessage = "색 그룹 편집을 다시 적용했습니다.";
            Repaint();
        }

        private static void PushHistory(List<int[]> history, int[] snapshot)
        {
            if (history.Count >= MaxHistorySteps)
            {
                history.RemoveAt(0);
            }

            history.Add(snapshot);
        }

        private static int[] PopHistory(List<int[]> history)
        {
            int lastIndex = history.Count - 1;
            int[] snapshot = history[lastIndex];
            history.RemoveAt(lastIndex);
            return snapshot;
        }

        private void ClearHistory()
        {
            undoHistory.Clear();
            redoHistory.Clear();
            paintStrokeBefore = null;
            paintStrokeChanged = false;
        }

        private void HandleKeyboardShortcuts()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown || !current.control)
            {
                return;
            }

            if (current.keyCode == KeyCode.Z)
            {
                if (current.shift)
                {
                    RedoPaint();
                }
                else
                {
                    UndoPaint();
                }

                current.Use();
            }
            else if (current.keyCode == KeyCode.Y)
            {
                RedoPaint();
                current.Use();
            }
        }

        private void SaveSplitModel(MeshSplitExportFormat requestedFormat, bool overwrite)
        {
            if (sourceData == null || groups.Count == 0)
            {
                return;
            }

            MeshSplitExportFormat format = requestedFormat;
            string targetAssetPath;
            if (overwrite)
            {
                if (!TryGetOverwriteTarget(out targetAssetPath, out format))
                {
                    EditorUtility.DisplayDialog(
                        "Mesh Split",
                        "Overwrite는 Assets 폴더의 FBX 또는 OBJ 원본에서만 사용할 수 있습니다.",
                        "OK");
                    return;
                }

                bool confirmed = EditorUtility.DisplayDialog(
                    "Mesh Split",
                    $"원본 파일을 분리 결과로 덮어씁니다. 기존 계층, 리그 및 애니메이션은 보존되지 않습니다.\n\n{targetAssetPath}",
                    "Overwrite",
                    "Cancel");
                if (!confirmed)
                {
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(outputFolderPath) || !AssetDatabase.IsValidFolder(outputFolderPath))
                {
                    EditorUtility.DisplayDialog("Mesh Split", "유효한 Assets 출력 폴더를 선택해주세요.", "OK");
                    return;
                }

                string safeOutputName = MeshSplitUtility.MakeSafeFileName(outputName);
                targetAssetPath = GetUniqueExportAssetPath(format, safeOutputName);
            }

            List<MeshSplitOutput> outputs = null;
            string absoluteTargetPath = MeshSplitExportUtility.GetAbsoluteProjectPath(targetAssetPath);
            try
            {
                List<Color> groupColors = new List<Color>(groups.Count);
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    groupColors.Add(groups[groupIndex].color);
                }

                outputs = MeshSplitUtility.BuildOutputs(sourceData, triangleGroups, groupColors, outputName);
                string safeName = MeshSplitUtility.MakeSafeFileName(outputName);
                if (overwrite)
                {
                    ExportToTemporaryAndReplace(format, absoluteTargetPath, outputs, safeName);
                }
                else
                {
                    ExportToFile(format, absoluteTargetPath, outputs, safeName);
                }

                AssetDatabase.ImportAsset(
                    targetAssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Object exportedAsset = AssetDatabase.LoadMainAssetAtPath(targetAssetPath);
                if (exportedAsset != null)
                {
                    Selection.activeObject = exportedAsset;
                    EditorGUIUtility.PingObject(exportedAsset);
                    if (overwrite)
                    {
                        LoadSource(exportedAsset);
                    }
                }

                statusMessage = overwrite
                    ? $"원본 {format.ToString().ToUpperInvariant()}를 덮어썼습니다: {targetAssetPath}"
                    : $"색상별 Mesh {outputs.Count}개를 {format.ToString().ToUpperInvariant()}로 저장했습니다: {targetAssetPath}";
                EditorUtility.SetDirty(this);
            }
            catch (Exception exception)
            {
                if (!overwrite)
                {
                    MeshSplitExportUtility.TryDeleteFile(absoluteTargetPath);
                    if (format == MeshSplitExportFormat.Obj)
                    {
                        MeshSplitExportUtility.TryDeleteFile(MeshSplitExportUtility.GetObjMaterialPath(absoluteTargetPath));
                    }
                }

                EditorUtility.DisplayDialog("Mesh Split Failed", exception.Message, "OK");
                statusMessage = exception.Message;
            }
            finally
            {
                DestroySplitOutputs(outputs);
            }
        }

        private static void ExportToFile(
            MeshSplitExportFormat format,
            string absolutePath,
            IReadOnlyList<MeshSplitOutput> outputs,
            string rootName)
        {
            if (format == MeshSplitExportFormat.Fbx)
            {
                MeshSplitExportUtility.ExportFbx(absolutePath, outputs, rootName);
            }
            else
            {
                MeshSplitExportUtility.ExportObj(absolutePath, outputs);
            }
        }

        private static void ExportToTemporaryAndReplace(
            MeshSplitExportFormat format,
            string absoluteTargetPath,
            IReadOnlyList<MeshSplitOutput> outputs,
            string rootName)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"ProjectF_MeshSplit_{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            string temporaryPath = Path.Combine(temporaryDirectory, Path.GetFileName(absoluteTargetPath));
            string temporaryMaterialPath = MeshSplitExportUtility.GetObjMaterialPath(temporaryPath);
            string backupPath = Path.Combine(temporaryDirectory, $"Original{MeshSplitExportUtility.GetExtension(format)}.backup");
            string backupMaterialPath = Path.Combine(temporaryDirectory, "Original.mtl.backup");
            string targetMaterialPath = MeshSplitExportUtility.GetObjMaterialPath(absoluteTargetPath);
            bool hadTargetMaterial = format == MeshSplitExportFormat.Obj && File.Exists(targetMaterialPath);
            bool materialReplaced = false;
            try
            {
                if (format == MeshSplitExportFormat.Fbx)
                {
                    MeshSplitExportUtility.ExportFbx(temporaryPath, outputs, rootName);
                }
                else
                {
                    string finalMaterialName = Path.GetFileName(targetMaterialPath);
                    MeshSplitExportUtility.ExportObj(temporaryPath, outputs, finalMaterialName);
                }

                if (format == MeshSplitExportFormat.Obj)
                {
                    if (hadTargetMaterial)
                    {
                        File.Replace(temporaryMaterialPath, targetMaterialPath, backupMaterialPath, true);
                    }
                    else
                    {
                        File.Move(temporaryMaterialPath, targetMaterialPath);
                    }

                    materialReplaced = true;
                }

                File.Replace(temporaryPath, absoluteTargetPath, backupPath, true);
            }
            catch
            {
                if (materialReplaced)
                {
                    if (hadTargetMaterial && File.Exists(backupMaterialPath))
                    {
                        File.Replace(backupMaterialPath, targetMaterialPath, null, true);
                    }
                    else
                    {
                        MeshSplitExportUtility.TryDeleteFile(targetMaterialPath);
                    }
                }

                throw;
            }
            finally
            {
                MeshSplitExportUtility.TryDeleteFile(temporaryPath);
                MeshSplitExportUtility.TryDeleteFile(temporaryMaterialPath);
                MeshSplitExportUtility.TryDeleteFile(backupPath);
                MeshSplitExportUtility.TryDeleteFile(backupMaterialPath);
                MeshSplitExportUtility.TryDeleteEmptyDirectory(temporaryDirectory);
            }
        }

        private string GetUniqueExportAssetPath(MeshSplitExportFormat format, string safeName)
        {
            string extension = MeshSplitExportUtility.GetExtension(format);
            for (int suffix = 0; suffix < int.MaxValue; suffix++)
            {
                string suffixText = suffix == 0 ? string.Empty : $"_{suffix}";
                string candidate = $"{outputFolderPath}/{safeName}{suffixText}{extension}";
                string absoluteCandidate = MeshSplitExportUtility.GetAbsoluteProjectPath(candidate);
                if (!File.Exists(absoluteCandidate)
                    && (format != MeshSplitExportFormat.Obj
                        || !File.Exists(MeshSplitExportUtility.GetObjMaterialPath(absoluteCandidate))))
                {
                    return candidate;
                }
            }

            throw new IOException($"사용 가능한 {format.ToString().ToUpperInvariant()} 출력 파일 이름을 찾지 못했습니다.");
        }

        private bool TryGetOverwriteTarget(out string assetPath, out MeshSplitExportFormat format)
        {
            assetPath = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset).Replace('\\', '/') : string.Empty;
            format = default;
            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string extension = Path.GetExtension(assetPath);
            if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase))
            {
                format = MeshSplitExportFormat.Fbx;
                return true;
            }

            if (string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase))
            {
                format = MeshSplitExportFormat.Obj;
                return true;
            }

            return false;
        }

        private static void DestroySplitOutputs(List<MeshSplitOutput> outputs)
        {
            if (outputs == null)
            {
                return;
            }

            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                Mesh mesh = outputs[outputIndex].Mesh;
                if (mesh != null)
                {
                    DestroyImmediate(mesh);
                }
            }
        }

        private static Color GenerateGroupColor(int index)
        {
            float hue = Mathf.Repeat(index * 0.61803398875f, 1f);
            float saturation = 0.68f + (index % 3) * 0.08f;
            float value = 0.9f - (index % 2) * 0.08f;
            Color color = Color.HSVToRGB(hue, Mathf.Clamp01(saturation), Mathf.Clamp01(value));
            color.a = 1f;
            return color;
        }

        private void DisposePreviewResources()
        {
            DestroyPreviewMesh();
            DestroyWireframeMesh();
            DestroyPreviewMaterials();
            DestroyWireframeMaterial();
            if (previewRenderer != null)
            {
                previewRenderer.Cleanup();
                previewRenderer = null;
            }
        }

        private void DestroyPreviewMesh()
        {
            if (previewMesh != null)
            {
                DestroyImmediate(previewMesh);
                previewMesh = null;
            }
        }

        private void DestroyWireframeMesh()
        {
            if (wireframeMesh != null)
            {
                DestroyImmediate(wireframeMesh);
                wireframeMesh = null;
            }
        }

        private void DestroyPreviewMaterials()
        {
            for (int materialIndex = 0; materialIndex < previewMaterials.Length; materialIndex++)
            {
                if (previewMaterials[materialIndex] != null)
                {
                    DestroyImmediate(previewMaterials[materialIndex]);
                }
            }

            previewMaterials = Array.Empty<Material>();
        }

        private void DestroyWireframeMaterial()
        {
            if (wireframeMaterial != null)
            {
                DestroyImmediate(wireframeMaterial);
                wireframeMaterial = null;
            }
        }
    }
}
