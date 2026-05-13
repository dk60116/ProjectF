using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class MeshTexturePainterWindow : EditorWindow
{
    private const float ToolbarWidth = 280f;
    private const float MinPreviewHeight = 360f;
    private const float DefaultBrushRadius = 12f;
    private const float MinBrushRadius = 1f;
    private const float MaxBrushRadius = 256f;
    private const int MaxHistorySteps = 32;
    private const int BrushPreviewSegments = 64;
    private const int MinUvViewTextureSize = 128;
    private const int MaxUvViewTextureSize = 1024;
    private const int MinUniqueUvTextureSize = 1024;
    private const int MaxUniqueUvTextureSize = 4096;
    private const int UniqueUvMinCellPixels = 64;
    private const int UniqueUvIslandPaddingPixels = 12;
    private const int UvTrianglePaintPaddingPixels = 8;

    private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly string[] UvPaintColorNames =
    {
        "White",
        "Black",
        "Red",
        "Orange",
        "Yellow",
        "Green",
        "Cyan",
        "Blue",
        "Purple",
        "Pink",
        "Gray"
    };

    private static readonly Color32[] UvPaintColors =
    {
        new Color32(255, 255, 255, 255),
        new Color32(24, 24, 24, 255),
        new Color32(230, 54, 64, 255),
        new Color32(240, 132, 40, 255),
        new Color32(245, 210, 64, 255),
        new Color32(76, 184, 92, 255),
        new Color32(64, 206, 214, 255),
        new Color32(72, 126, 230, 255),
        new Color32(142, 90, 222, 255),
        new Color32(232, 102, 170, 255),
        new Color32(128, 128, 128, 255)
    };

    private Mesh mesh;
    private Texture2D sourceTexture;
    private Texture2D workingTexture;
    private Texture2D uvViewTexture;
    private PreviewRenderUtility previewRenderer;
    private Material previewMaterial;

    private Color brushColor = Color.white;
    private float brushRadius = DefaultBrushRadius;
    private float brushOpacity = 1f;
    private float brushHardness = 0.65f;
    private int uvPaintColorIndex = 2;
    private bool showUvView;
    private bool textureDirty;
    private bool uvViewNeedsRebuild = true;
    private readonly List<TextureSnapshot> undoHistory = new List<TextureSnapshot>();
    private readonly List<TextureSnapshot> redoHistory = new List<TextureSnapshot>();
    private bool paintStrokeUndoCaptured;
    private bool paintStrokeChanged;

    private Vector2 previewAngles = new Vector2(35f, -35f);
    private Vector2 previewPan;
    private float previewDistanceScale = 1.35f;
    private string statusMessage = "Load a mesh and texture, then paint on the preview.";
    private bool hasBrushPreview;
    private MeshRaycastHit brushPreviewHit;

    private readonly struct TextureSnapshot
    {
        public TextureSnapshot(int width, int height, Color32[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public Color32[] Pixels { get; }
    }

    private readonly struct MeshRaycastHit
    {
        public MeshRaycastHit(
            Vector3 point,
            Vector3 vertex0,
            Vector3 vertex1,
            Vector3 vertex2,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2)
        {
            Point = point;
            Vertex0 = vertex0;
            Vertex1 = vertex1;
            Vertex2 = vertex2;
            Uv0 = uv0;
            Uv1 = uv1;
            Uv2 = uv2;
        }

        public Vector3 Point { get; }
        public Vector3 Vertex0 { get; }
        public Vector3 Vertex1 { get; }
        public Vector3 Vertex2 { get; }
        public Vector2 Uv0 { get; }
        public Vector2 Uv1 { get; }
        public Vector2 Uv2 { get; }
    }

    private readonly struct UvTriangleInfo
    {
        public UvTriangleInfo(Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            Uv0 = uv0;
            Uv1 = uv1;
            Uv2 = uv2;
        }

        public Vector2 Uv0 { get; }
        public Vector2 Uv1 { get; }
        public Vector2 Uv2 { get; }
    }

    private readonly struct UvTriangleKey : IEquatable<UvTriangleKey>
    {
        public UvTriangleKey(Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            ulong p0 = QuantizeUvPoint(uv0);
            ulong p1 = QuantizeUvPoint(uv1);
            ulong p2 = QuantizeUvPoint(uv2);
            Sort(ref p0, ref p1, ref p2);
            Point0 = p0;
            Point1 = p1;
            Point2 = p2;
        }

        public ulong Point0 { get; }
        public ulong Point1 { get; }
        public ulong Point2 { get; }

        public bool Equals(UvTriangleKey other)
        {
            return Point0 == other.Point0 && Point1 == other.Point1 && Point2 == other.Point2;
        }

        public override bool Equals(object obj)
        {
            return obj is UvTriangleKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Point0.GetHashCode();
                hash = (hash * 397) ^ Point1.GetHashCode();
                hash = (hash * 397) ^ Point2.GetHashCode();
                return hash;
            }
        }

        private static void Sort(ref ulong a, ref ulong b, ref ulong c)
        {
            if (a > b)
            {
                Swap(ref a, ref b);
            }

            if (b > c)
            {
                Swap(ref b, ref c);
            }

            if (a > b)
            {
                Swap(ref a, ref b);
            }
        }

        private static void Swap(ref ulong a, ref ulong b)
        {
            ulong temp = a;
            a = b;
            b = temp;
        }
    }

    [MenuItem("Window/ProjectF/Mesh Texture Painter")]
    [MenuItem("Tools/MapObject/Mesh Texture Painter")]
    public static void ShowWindow()
    {
        MeshTexturePainterWindow window = GetWindow<MeshTexturePainterWindow>("Mesh Texture Painter");
        window.minSize = new Vector2(760f, 460f);
        window.Show();
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        EnsurePreviewRenderer();
        EnsurePreviewMaterial();
    }

    private void OnDisable()
    {
        if (previewRenderer != null)
        {
            previewRenderer.Cleanup();
            previewRenderer = null;
        }

        if (previewMaterial != null)
        {
            DestroyImmediate(previewMaterial);
            previewMaterial = null;
        }

        DestroyUvViewTexture();
        DestroyWorkingTexture();
    }

    private void OnGUI()
    {
        EnsurePreviewRenderer();
        EnsurePreviewMaterial();
        HandleKeyboardShortcuts();

        EditorGUILayout.BeginHorizontal();
        DrawToolbar();
        DrawPreviewPanel();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(ToolbarWidth));
        EditorGUILayout.LabelField("Mesh Texture Painter", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        Mesh nextMesh = EditorGUILayout.ObjectField("Mesh", mesh, typeof(Mesh), false) as Mesh;
        Texture2D nextTexture = EditorGUILayout.ObjectField("Texture", sourceTexture, typeof(Texture2D), false) as Texture2D;
        if (EditorGUI.EndChangeCheck())
        {
            bool meshChanged = mesh != nextMesh;
            mesh = nextMesh;
            if (meshChanged)
            {
                MarkUvViewDirty();
            }

            if (sourceTexture != nextTexture)
            {
                sourceTexture = nextTexture;
                ReloadWorkingTexture();
            }
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load Selected"))
        {
            LoadSelection();
        }

        EditorGUI.BeginDisabledGroup(sourceTexture == null);
        if (GUILayout.Button("Reload Texture"))
        {
            ReloadWorkingTexture();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(!CanUndo);
        if (GUILayout.Button("Undo"))
        {
            UndoPaint();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!CanRedo);
        if (GUILayout.Button("Redo"))
        {
            RedoPaint();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("View", EditorStyles.boldLabel);
        bool nextShowUvView = GUILayout.Toggle(
            showUvView,
            showUvView ? "UV View: ON" : "UV View: OFF",
            EditorStyles.miniButton,
            GUILayout.Height(24f));
        if (showUvView != nextShowUvView)
        {
            SetUvViewVisible(nextShowUvView);
        }

        EditorGUILayout.Space(8f);
        if (showUvView)
        {
            DrawUvEditControls();
        }
        else
        {
            DrawBrushControls();
        }

        EditorGUILayout.Space(8f);
        EditorGUI.BeginDisabledGroup(workingTexture == null);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            SaveWorkingTexture();
        }

        if (GUILayout.Button("Save As PNG"))
        {
            SaveWorkingTextureAsPng();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8f);
        DrawTexturePreview();

        GUILayout.FlexibleSpace();
        EditorGUILayout.HelpBox(
            showUvView
                ? "Left click: fill UV triangle\nRight drag: rotate\nMiddle drag: pan\nWheel: zoom\nCtrl+Z: undo\nCtrl+Y / Ctrl+Shift+Z: redo"
                : "Left drag: paint\nRight drag: rotate\nMiddle drag: pan\nWheel: zoom\nCtrl+Z: undo\nCtrl+Y / Ctrl+Shift+Z: redo",
            MessageType.None);
        EditorGUILayout.HelpBox(statusMessage, textureDirty ? MessageType.Warning : MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawBrushControls()
    {
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        brushColor = EditorGUILayout.ColorField("Color", brushColor);
        brushRadius = EditorGUILayout.Slider("Radius (px)", brushRadius, MinBrushRadius, MaxBrushRadius);
        brushOpacity = EditorGUILayout.Slider("Opacity", brushOpacity, 0f, 1f);
        brushHardness = EditorGUILayout.Slider("Hardness", brushHardness, 0f, 1f);
    }

    private void DrawUvEditControls()
    {
        EditorGUILayout.LabelField("UV Edit", EditorStyles.boldLabel);
        uvPaintColorIndex = EditorGUILayout.Popup("Color", uvPaintColorIndex, UvPaintColorNames);
        Rect swatchRect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(swatchRect, SelectedUvPaintColor);

        EditorGUILayout.Space(4f);
        EditorGUI.BeginDisabledGroup(mesh == null);
        if (GUILayout.Button("Create Unique UV Mesh", GUILayout.Height(24f)))
        {
            CreateUniqueUvMeshAsset();
        }
        EditorGUI.EndDisabledGroup();
    }

    private bool CanUndo => workingTexture != null && undoHistory.Count > 0;

    private bool CanRedo => workingTexture != null && redoHistory.Count > 0;

    private Color32 SelectedUvPaintColor => UvPaintColors[Mathf.Clamp(uvPaintColorIndex, 0, UvPaintColors.Length - 1)];

    private void SetUvViewVisible(bool visible)
    {
        showUvView = visible;
        if (showUvView)
        {
            MarkUvViewDirty();
        }

        ApplyPreviewTexture();
        Repaint();
    }

    private void HandleKeyboardShortcuts()
    {
        Event current = Event.current;
        if (current == null || current.type != EventType.KeyDown)
        {
            return;
        }

        if (!current.control && !current.command)
        {
            return;
        }

        if (current.keyCode == KeyCode.Z)
        {
            if (current.shift && CanRedo)
            {
                RedoPaint();
                current.Use();
                return;
            }

            if (!current.shift && CanUndo)
            {
                UndoPaint();
                current.Use();
            }

            return;
        }

        if (current.keyCode == KeyCode.Y && CanRedo)
        {
            RedoPaint();
            current.Use();
        }
    }

    private void DrawTexturePreview()
    {
        Texture2D displayTexture = ResolveDisplayTexture();
        if (displayTexture == null)
        {
            return;
        }

        Rect rect = GUILayoutUtility.GetRect(1f, 180f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
        GUI.DrawTexture(rect, displayTexture, ScaleMode.ScaleToFit, true);
    }

    private Texture2D ResolveDisplayTexture()
    {
        if (showUvView)
        {
            EnsureUvViewTexture();
            if (uvViewTexture != null)
            {
                return uvViewTexture;
            }
        }

        return workingTexture != null ? workingTexture : sourceTexture;
    }

    private void DrawPreviewPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        Rect previewRect = GUILayoutUtility.GetRect(
            1f,
            MinPreviewHeight,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        EditorGUI.DrawRect(previewRect, new Color(0.09f, 0.09f, 0.1f, 1f));
        SetupPreviewCamera(previewRect);
        HandlePreviewInput(previewRect);

        if (Event.current.type == EventType.Repaint)
        {
            RenderPreview(previewRect);
            DrawBrushPreviewOverlay(previewRect);
        }

        EditorGUILayout.EndVertical();
    }

    private void EnsurePreviewRenderer()
    {
        if (previewRenderer != null)
        {
            return;
        }

        previewRenderer = new PreviewRenderUtility();
        previewRenderer.camera.clearFlags = CameraClearFlags.Color;
        previewRenderer.camera.backgroundColor = new Color(0.09f, 0.09f, 0.1f, 1f);
        previewRenderer.cameraFieldOfView = 30f;

        if (previewRenderer.lights != null && previewRenderer.lights.Length >= 2)
        {
            previewRenderer.lights[0].intensity = 1.25f;
            previewRenderer.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            previewRenderer.lights[1].intensity = 0.8f;
        }
    }

    private void EnsurePreviewMaterial()
    {
        if (previewMaterial != null)
        {
            ApplyPreviewTexture();
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return;
        }

        previewMaterial = new Material(shader)
        {
            name = "MeshTexturePainterPreviewMaterial",
            hideFlags = HideFlags.HideAndDontSave
        };
        ApplyPreviewTexture();
    }

    private void ApplyPreviewTexture()
    {
        if (previewMaterial == null)
        {
            return;
        }

        Texture texture = ResolvePreviewTexture();
        if (previewMaterial.HasProperty(BaseMapPropertyId))
        {
            previewMaterial.SetTexture(BaseMapPropertyId, texture);
        }

        if (previewMaterial.HasProperty(MainTexPropertyId))
        {
            previewMaterial.SetTexture(MainTexPropertyId, texture);
        }

        if (previewMaterial.HasProperty(BaseColorPropertyId))
        {
            previewMaterial.SetColor(BaseColorPropertyId, Color.white);
        }

        if (previewMaterial.HasProperty(ColorPropertyId))
        {
            previewMaterial.SetColor(ColorPropertyId, Color.white);
        }
    }

    private Texture ResolvePreviewTexture()
    {
        if (showUvView)
        {
            EnsureUvViewTexture();
            if (uvViewTexture != null)
            {
                return uvViewTexture;
            }
        }

        return workingTexture != null ? workingTexture : Texture2D.whiteTexture;
    }

    private void EnsureUvViewTexture()
    {
        if (!showUvView || !uvViewNeedsRebuild)
        {
            return;
        }

        DestroyUvViewTexture();
        uvViewNeedsRebuild = false;
        uvViewTexture = CreateUvViewTexture();
    }

    private Texture2D CreateUvViewTexture()
    {
        if (!TryReadMeshUvTriangles(mesh, out UvTriangleInfo[] triangles))
        {
            statusMessage = "UV edit requires a readable mesh with UVs.";
            return null;
        }

        ResolveUvViewSize(out int width, out int height);
        Color32 edgeColor = new Color32(18, 18, 20, 255);
        Color32[] pixels = CreateUvEditBasePixels(width, height);
        int[] sharedGroupIndices = BuildSharedUvTriangleGroupIndices(triangles, out int sharedGroupCount);

        for (int i = 0; i < triangles.Length; i++)
        {
            if (sharedGroupIndices[i] < 0)
            {
                continue;
            }

            RasterizeUvTriangleOverlay(pixels, width, height, triangles[i], ResolveSharedUvGroupColor(sharedGroupIndices[i]), 0.55f);
        }

        for (int i = 0; i < triangles.Length; i++)
        {
            if (sharedGroupIndices[i] < 0)
            {
                continue;
            }

            DrawUvTriangleEdges(pixels, width, height, triangles[i], edgeColor);
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "MeshTexturePainterUvView",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        if (sharedGroupCount == 0)
        {
            statusMessage = "No shared UV triangles found.";
        }
        else if (workingTexture == null)
        {
            statusMessage = $"Shared UV groups: {sharedGroupCount}. Load a texture to paint and save.";
        }
        else
        {
            statusMessage = $"Shared UV groups: {sharedGroupCount}.";
        }

        return texture;
    }

    private Color32[] CreateUvEditBasePixels(int width, int height)
    {
        Color32[] pixels = new Color32[width * height];
        if (workingTexture == null)
        {
            FillCheckerboard(pixels, width, height);
            return pixels;
        }

        Color32[] sourcePixels = workingTexture.GetPixels32();
        int sourceWidth = workingTexture.width;
        int sourceHeight = workingTexture.height;
        for (int y = 0; y < height; y++)
        {
            int sourceY = height <= 1
                ? 0
                : Mathf.Clamp(Mathf.RoundToInt((y / (height - 1f)) * (sourceHeight - 1)), 0, sourceHeight - 1);
            for (int x = 0; x < width; x++)
            {
                int sourceX = width <= 1
                    ? 0
                    : Mathf.Clamp(Mathf.RoundToInt((x / (width - 1f)) * (sourceWidth - 1)), 0, sourceWidth - 1);
                pixels[(y * width) + x] = MultiplyColor(sourcePixels[(sourceY * sourceWidth) + sourceX], 0.72f);
            }
        }

        return pixels;
    }

    private static void FillCheckerboard(Color32[] pixels, int width, int height)
    {
        Color32 colorA = new Color32(52, 52, 58, 255);
        Color32 colorB = new Color32(38, 38, 44, 255);
        const int cellSize = 16;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool alternate = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                pixels[(y * width) + x] = alternate ? colorA : colorB;
            }
        }
    }

    private static Color32 MultiplyColor(Color32 color, float multiplier)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * multiplier), 0, 255),
            color.a);
    }

    private static int[] BuildSharedUvTriangleGroupIndices(UvTriangleInfo[] triangles, out int sharedGroupCount)
    {
        sharedGroupCount = 0;
        int[] groupIndices = new int[triangles.Length];
        for (int i = 0; i < groupIndices.Length; i++)
        {
            groupIndices[i] = -1;
        }

        Dictionary<UvTriangleKey, List<int>> buckets = new Dictionary<UvTriangleKey, List<int>>();
        for (int i = 0; i < triangles.Length; i++)
        {
            UvTriangleKey key = new UvTriangleKey(triangles[i].Uv0, triangles[i].Uv1, triangles[i].Uv2);
            if (!buckets.TryGetValue(key, out List<int> indices))
            {
                indices = new List<int>();
                buckets.Add(key, indices);
            }

            indices.Add(i);
        }

        foreach (KeyValuePair<UvTriangleKey, List<int>> pair in buckets)
        {
            if (pair.Value.Count < 2)
            {
                continue;
            }

            for (int i = 0; i < pair.Value.Count; i++)
            {
                groupIndices[pair.Value[i]] = sharedGroupCount;
            }

            sharedGroupCount++;
        }

        return groupIndices;
    }

    private static Color32 ResolveSharedUvGroupColor(int groupIndex)
    {
        float hue = Mathf.Repeat(groupIndex * 0.61803398875f, 1f);
        Color color = Color.HSVToRGB(hue, 0.7f, 1f);
        return new Color32(
            (byte)Mathf.RoundToInt(color.r * 255f),
            (byte)Mathf.RoundToInt(color.g * 255f),
            (byte)Mathf.RoundToInt(color.b * 255f),
            255);
    }

    private static ulong QuantizeUvPoint(Vector2 uv)
    {
        uint x = (uint)Mathf.Clamp(Mathf.RoundToInt(NormalizeUv01(uv.x) * 8192f), 0, 8192);
        uint y = (uint)Mathf.Clamp(Mathf.RoundToInt(NormalizeUv01(uv.y) * 8192f), 0, 8192);
        return ((ulong)x << 32) | y;
    }

    private static bool TryReadMeshUvTriangles(Mesh targetMesh, out UvTriangleInfo[] triangles)
    {
        triangles = null;
        if (targetMesh == null)
        {
            return false;
        }

        Vector2[] uvs;
        int[] indices;
        try
        {
            uvs = targetMesh.uv;
            indices = targetMesh.triangles;
        }
        catch (Exception)
        {
            return false;
        }

        if (uvs == null || indices == null || uvs.Length == 0 || indices.Length < 3)
        {
            return false;
        }

        List<UvTriangleInfo> result = new List<UvTriangleInfo>(indices.Length / 3);
        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0
                || i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length)
            {
                continue;
            }

            result.Add(new UvTriangleInfo(
                NormalizeUv01(uvs[i0]),
                NormalizeUv01(uvs[i1]),
                NormalizeUv01(uvs[i2])));
        }

        if (result.Count == 0)
        {
            return false;
        }

        triangles = result.ToArray();
        return true;
    }

    private void ResolveUvViewSize(out int width, out int height)
    {
        int sourceWidth = workingTexture != null
            ? workingTexture.width
            : sourceTexture != null ? sourceTexture.width : 512;
        int sourceHeight = workingTexture != null
            ? workingTexture.height
            : sourceTexture != null ? sourceTexture.height : 512;
        sourceWidth = Mathf.Max(1, sourceWidth);
        sourceHeight = Mathf.Max(1, sourceHeight);

        float scale = Mathf.Min(1f, MaxUvViewTextureSize / (float)Mathf.Max(sourceWidth, sourceHeight));
        width = Mathf.Clamp(Mathf.RoundToInt(sourceWidth * scale), MinUvViewTextureSize, MaxUvViewTextureSize);
        height = Mathf.Clamp(Mathf.RoundToInt(sourceHeight * scale), MinUvViewTextureSize, MaxUvViewTextureSize);
    }

    private static bool RasterizeUvTriangle(
        Color32[] pixels,
        int width,
        int height,
        UvTriangleInfo triangle,
        Color32 color,
        int paddingPixels = 0)
    {
        Vector2 p0 = UvToTexturePoint(triangle.Uv0, width, height);
        Vector2 p1 = UvToTexturePoint(triangle.Uv1, width, height);
        Vector2 p2 = UvToTexturePoint(triangle.Uv2, width, height);
        float area = EdgeFunction(p0, p1, p2);
        if (Mathf.Abs(area) < 0.000001f)
        {
            return false;
        }

        int padding = Mathf.Max(0, paddingPixels);
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))) - padding, 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))) + padding, 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))) - padding, 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))) + padding, 0, height - 1);
        bool changed = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = EdgeFunction(p1, p2, point) / area;
                float w1 = EdgeFunction(p2, p0, point) / area;
                float w2 = EdgeFunction(p0, p1, point) / area;
                bool insideTriangle = w0 >= -0.0001f && w1 >= -0.0001f && w2 >= -0.0001f;
                bool insidePadding = padding > 0 && DistanceToTriangleEdge(point, p0, p1, p2) <= padding;
                if (insideTriangle || insidePadding)
                {
                    int pixelIndex = (y * width) + x;
                    if (!pixels[pixelIndex].Equals(color))
                    {
                        pixels[pixelIndex] = color;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    private static float DistanceToTriangleEdge(Vector2 point, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        return Mathf.Min(
            DistanceToSegment(point, p0, p1),
            Mathf.Min(DistanceToSegment(point, p1, p2), DistanceToSegment(point, p2, p0)));
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr <= 0.000001f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
        return Vector2.Distance(point, a + (ab * t));
    }

    private static void RasterizeUvTriangleOverlay(
        Color32[] pixels,
        int width,
        int height,
        UvTriangleInfo triangle,
        Color32 color,
        float alpha)
    {
        Vector2 p0 = UvToTexturePoint(triangle.Uv0, width, height);
        Vector2 p1 = UvToTexturePoint(triangle.Uv1, width, height);
        Vector2 p2 = UvToTexturePoint(triangle.Uv2, width, height);
        float area = EdgeFunction(p0, p1, p2);
        if (Mathf.Abs(area) < 0.000001f)
        {
            return;
        }

        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, height - 1);
        float clampedAlpha = Mathf.Clamp01(alpha);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = EdgeFunction(p1, p2, point) / area;
                float w1 = EdgeFunction(p2, p0, point) / area;
                float w2 = EdgeFunction(p0, p1, point) / area;
                if (w0 >= -0.0001f && w1 >= -0.0001f && w2 >= -0.0001f)
                {
                    int pixelIndex = (y * width) + x;
                    pixels[pixelIndex] = BlendColor(pixels[pixelIndex], color, clampedAlpha);
                }
            }
        }
    }

    private static Color32 BlendColor(Color32 baseColor, Color32 overlayColor, float alpha)
    {
        float inverseAlpha = 1f - alpha;
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt((baseColor.r * inverseAlpha) + (overlayColor.r * alpha)), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt((baseColor.g * inverseAlpha) + (overlayColor.g * alpha)), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt((baseColor.b * inverseAlpha) + (overlayColor.b * alpha)), 0, 255),
            baseColor.a);
    }

    private static void DrawUvTriangleEdges(Color32[] pixels, int width, int height, UvTriangleInfo triangle, Color32 color)
    {
        Vector2Int p0 = Vector2Int.RoundToInt(UvToTexturePoint(triangle.Uv0, width, height));
        Vector2Int p1 = Vector2Int.RoundToInt(UvToTexturePoint(triangle.Uv1, width, height));
        Vector2Int p2 = Vector2Int.RoundToInt(UvToTexturePoint(triangle.Uv2, width, height));
        DrawLine(pixels, width, height, p0, p1, color);
        DrawLine(pixels, width, height, p1, p2, color);
        DrawLine(pixels, width, height, p2, p0, color);
    }

    private static void DrawLine(Color32[] pixels, int width, int height, Vector2Int from, Vector2Int to, Color32 color)
    {
        int x0 = from.x;
        int y0 = from.y;
        int x1 = to.x;
        int y1 = to.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int error = dx - dy;

        while (true)
        {
            SetPixelIfInBounds(pixels, width, height, x0, y0, color);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int doubleError = error * 2;
            if (doubleError > -dy)
            {
                error -= dy;
                x0 += sx;
            }

            if (doubleError < dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void SetPixelIfInBounds(Color32[] pixels, int width, int height, int x, int y, Color32 color)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        pixels[(y * width) + x] = color;
    }

    private static Vector2 UvToTexturePoint(Vector2 uv, int width, int height)
    {
        return new Vector2(
            Mathf.Clamp(uv.x * (width - 1), 0f, width - 1),
            Mathf.Clamp(uv.y * (height - 1), 0f, height - 1));
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((c.x - a.x) * (b.y - a.y)) - ((c.y - a.y) * (b.x - a.x));
    }

    private static Vector2 NormalizeUv01(Vector2 uv)
    {
        return new Vector2(NormalizeUv01(uv.x), NormalizeUv01(uv.y));
    }

    private static float NormalizeUv01(float uv)
    {
        return uv >= 0f && uv <= 1f ? uv : Mathf.Repeat(uv, 1f);
    }

    private void MarkUvViewDirty()
    {
        uvViewNeedsRebuild = true;
        DestroyUvViewTexture();
    }

    private void DestroyUvViewTexture()
    {
        if (uvViewTexture == null)
        {
            return;
        }

        DestroyImmediate(uvViewTexture);
        uvViewTexture = null;
    }

    private void SetupPreviewCamera(Rect previewRect)
    {
        if (previewRenderer == null)
        {
            return;
        }

        Bounds bounds = ResolveMeshBounds();
        float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
        float distance = Mathf.Max(0.3f, radius * Mathf.Clamp(previewDistanceScale, 0.2f, 8f));
        Quaternion rotation = Quaternion.Euler(previewAngles.x, previewAngles.y, 0f);
        Camera camera = previewRenderer.camera;
        Vector3 panOffset = (camera.transform.right * previewPan.x) + (camera.transform.up * previewPan.y);
        Vector3 center = bounds.center + panOffset;

        camera.transform.rotation = rotation;
        camera.transform.position = center - (rotation * Vector3.forward * distance);
        camera.nearClipPlane = Mathf.Max(0.001f, distance * 0.01f);
        camera.farClipPlane = Mathf.Max(10f, distance + radius * 6f);
        camera.aspect = previewRect.height > 0f ? previewRect.width / previewRect.height : 1f;
    }

    private Bounds ResolveMeshBounds()
    {
        if (mesh != null)
        {
            Bounds meshBounds = mesh.bounds;
            if (meshBounds.size.sqrMagnitude > 0.0001f)
            {
                return meshBounds;
            }
        }

        return new Bounds(Vector3.zero, Vector3.one);
    }

    private void RenderPreview(Rect previewRect)
    {
        if (previewRenderer == null)
        {
            return;
        }

        previewRenderer.BeginPreview(previewRect, GUIStyle.none);
        if (mesh != null && previewMaterial != null)
        {
            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            for (int i = 0; i < subMeshCount; i++)
            {
                previewRenderer.DrawMesh(mesh, Matrix4x4.identity, previewMaterial, i);
            }
        }

        previewRenderer.camera.Render();
        Texture previewTexture = previewRenderer.EndPreview();
        GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, false);
    }

    private void DrawBrushPreviewOverlay(Rect previewRect)
    {
        if (!hasBrushPreview || previewRenderer == null || previewRenderer.camera == null)
        {
            return;
        }

        if (showUvView)
        {
            DrawUvTrianglePreviewOverlay(previewRect);
            return;
        }

        if (workingTexture == null)
        {
            return;
        }

        if (!TryWorldToPreviewGuiPoint(previewRect, brushPreviewHit.Point, out Vector2 center))
        {
            return;
        }

        float radius = Mathf.Max(MinBrushRadius, brushRadius);
        Vector3[] outerPoints = BuildScreenSpaceCircle(center, radius);
        Color outlineColor = ResolveBrushPreviewColor(0.95f);
        Color fillColor = ResolveBrushPreviewColor(0.12f);
        Handles.BeginGUI();
        Color previousColor = Handles.color;
        Handles.color = fillColor;
        Handles.DrawAAConvexPolygon(outerPoints);
        Handles.color = outlineColor;
        Handles.DrawAAPolyLine(2.5f, ClosePolygon(outerPoints));

        float innerRadius = Mathf.Max(0f, brushRadius * Mathf.Clamp01(brushHardness));
        if (innerRadius >= MinBrushRadius && innerRadius < radius)
        {
            Vector3[] innerPoints = BuildScreenSpaceCircle(center, innerRadius);
            Handles.color = ResolveBrushPreviewColor(0.35f);
            Handles.DrawAAPolyLine(1.25f, ClosePolygon(innerPoints));
        }

        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void DrawUvTrianglePreviewOverlay(Rect previewRect)
    {
        if (!TryWorldToPreviewGuiPoint(previewRect, brushPreviewHit.Vertex0, out Vector2 p0)
            || !TryWorldToPreviewGuiPoint(previewRect, brushPreviewHit.Vertex1, out Vector2 p1)
            || !TryWorldToPreviewGuiPoint(previewRect, brushPreviewHit.Vertex2, out Vector2 p2))
        {
            return;
        }

        Vector3[] points = { p0, p1, p2 };
        Color fillColor = SelectedUvPaintColor;
        fillColor.a = 0.22f;
        Color outlineColor = SelectedUvPaintColor;
        outlineColor.a = 0.95f;

        Handles.BeginGUI();
        Color previousColor = Handles.color;
        Handles.color = fillColor;
        Handles.DrawAAConvexPolygon(points);
        Handles.color = outlineColor;
        Handles.DrawAAPolyLine(2.5f, ClosePolygon(points));
        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private Color ResolveBrushPreviewColor(float alpha)
    {
        Color.RGBToHSV(brushColor, out _, out float saturation, out float value);
        Color color = saturation < 0.08f && value > 0.85f
            ? new Color(0.1f, 0.9f, 1f, alpha)
            : brushColor;
        color.a = alpha;
        return color;
    }

    private bool TryWorldToPreviewGuiPoint(Rect previewRect, Vector3 worldPoint, out Vector2 guiPoint)
    {
        guiPoint = Vector2.zero;
        Camera camera = previewRenderer.camera;
        Vector3 viewportPoint = camera.WorldToViewportPoint(worldPoint);
        if (viewportPoint.z <= 0f)
        {
            return false;
        }

        guiPoint = new Vector2(
            previewRect.x + (viewportPoint.x * previewRect.width),
            previewRect.y + ((1f - viewportPoint.y) * previewRect.height));
        return true;
    }

    private static Vector3[] ClosePolygon(Vector3[] points)
    {
        Vector3[] closedPoints = new Vector3[points.Length + 1];
        Array.Copy(points, closedPoints, points.Length);
        closedPoints[closedPoints.Length - 1] = points[0];
        return closedPoints;
    }

    private static Vector3[] BuildScreenSpaceCircle(Vector2 center, float radius)
    {
        Vector3[] points = new Vector3[BrushPreviewSegments];
        for (int i = 0; i < BrushPreviewSegments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / BrushPreviewSegments;
            points[i] = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        return points;
    }

    private void HandlePreviewInput(Rect previewRect)
    {
        Event current = Event.current;
        if (current == null)
        {
            return;
        }

        int controlId = GUIUtility.GetControlID("MeshTexturePainterPreview".GetHashCode(), FocusType.Passive, previewRect);
        bool isInsidePreview = previewRect.Contains(current.mousePosition);

        if (isInsidePreview && (current.type == EventType.MouseMove || current.type == EventType.Repaint))
        {
            UpdateBrushPreview(previewRect, current.mousePosition);
            if (current.type == EventType.MouseMove)
            {
                Repaint();
            }
        }
        else if (!isInsidePreview && GUIUtility.hotControl != controlId)
        {
            hasBrushPreview = false;
        }

        if (current.type == EventType.ScrollWheel && isInsidePreview)
        {
            previewDistanceScale = Mathf.Clamp(previewDistanceScale * (1f + current.delta.y * 0.08f), 0.2f, 8f);
            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseDown && isInsidePreview)
        {
            GUIUtility.hotControl = controlId;
            if (current.button == 0)
            {
                paintStrokeUndoCaptured = false;
                paintStrokeChanged = false;
                PaintAtMousePosition(previewRect, current.mousePosition);
            }

            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
        {
            if (current.button == 0 && !showUvView)
            {
                PaintAtMousePosition(previewRect, current.mousePosition);
            }
            else if (current.button == 1)
            {
                previewAngles.y += current.delta.x * 0.4f;
                previewAngles.x = Mathf.Clamp(previewAngles.x - current.delta.y * 0.4f, -89f, 89f);
                UpdateBrushPreview(previewRect, current.mousePosition);
            }
            else if (current.button == 2)
            {
                float panScale = ResolveMeshBounds().extents.magnitude * 0.0025f;
                previewPan.x -= current.delta.x * panScale;
                previewPan.y += current.delta.y * panScale;
                UpdateBrushPreview(previewRect, current.mousePosition);
            }

            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
        {
            if (current.button == 0 && !showUvView)
            {
                FinishPaintStroke();
            }

            GUIUtility.hotControl = 0;
            current.Use();
            Repaint();
        }
    }

    private void UpdateBrushPreview(Rect previewRect, Vector2 mousePosition)
    {
        if (mesh == null || !previewRect.Contains(mousePosition) || (!showUvView && workingTexture == null))
        {
            hasBrushPreview = false;
            return;
        }

        if (!TryBuildPreviewRay(previewRect, mousePosition, out Ray ray)
            || !TryRaycastMesh(mesh, ray, out MeshRaycastHit hit))
        {
            hasBrushPreview = false;
            return;
        }

        brushPreviewHit = hit;
        hasBrushPreview = true;
    }

    private void PaintAtMousePosition(Rect previewRect, Vector2 mousePosition)
    {
        if (mesh == null || (!showUvView && workingTexture == null))
        {
            statusMessage = "Mesh and texture are required.";
            return;
        }

        if (!TryBuildPreviewRay(previewRect, mousePosition, out Ray ray))
        {
            return;
        }

        if (!TryRaycastMesh(mesh, ray, out MeshRaycastHit hit))
        {
            statusMessage = "No mesh surface under the cursor.";
            hasBrushPreview = false;
            return;
        }

        brushPreviewHit = hit;
        hasBrushPreview = true;

        if (showUvView)
        {
            PaintUvTriangle(hit);
            return;
        }

        if (!paintStrokeUndoCaptured)
        {
            RecordUndoState();
            paintStrokeUndoCaptured = true;
        }

        if (PaintTextureAtScreenBrush(previewRect, mousePosition))
        {
            if (!paintStrokeChanged)
            {
                redoHistory.Clear();
            }

            paintStrokeChanged = true;
        }
    }

    private void PaintUvTriangle(MeshRaycastHit hit)
    {
        if (workingTexture == null)
        {
            statusMessage = "UV edit requires a texture.";
            return;
        }

        TextureSnapshot snapshot = CaptureWorkingTexture();
        if (snapshot.Pixels == null || snapshot.Pixels.Length == 0)
        {
            return;
        }

        Color32[] pixels = workingTexture.GetPixels32();
        UvTriangleInfo triangle = new UvTriangleInfo(
            NormalizeUv01(hit.Uv0),
            NormalizeUv01(hit.Uv1),
            NormalizeUv01(hit.Uv2));
        if (!RasterizeUvTriangle(
                pixels,
                workingTexture.width,
                workingTexture.height,
                triangle,
                SelectedUvPaintColor,
                UvTrianglePaintPaddingPixels))
        {
            statusMessage = "UV triangle made no visible change.";
            return;
        }

        PushSnapshot(undoHistory, snapshot);
        redoHistory.Clear();
        workingTexture.SetPixels32(pixels);
        workingTexture.Apply(false, false);
        textureDirty = true;
        MarkUvViewDirty();
        ApplyPreviewTexture();
        statusMessage = $"Painted UV triangle: {UvPaintColorNames[Mathf.Clamp(uvPaintColorIndex, 0, UvPaintColorNames.Length - 1)]}.";
        Repaint();
    }

    private void CreateUniqueUvMeshAsset()
    {
        if (mesh == null)
        {
            statusMessage = "Mesh is required.";
            return;
        }

        Mesh uniqueMesh;
        int textureSize;
        try
        {
            int triangleCount = CountSourceTriangles(mesh, Mathf.Max(1, mesh.subMeshCount));
            textureSize = ResolveUniqueUvTextureSize(triangleCount);
            uniqueMesh = BuildUniqueUvMesh(mesh, textureSize);
        }
        catch (Exception exception)
        {
            statusMessage = $"Failed to create unique UV mesh: {exception.Message}";
            Debug.LogException(exception);
            return;
        }

        if (uniqueMesh == null)
        {
            statusMessage = "Failed to create unique UV mesh.";
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(mesh);
        string folderPath = "Assets";
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            string directory = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                folderPath = directory.Replace("\\", "/");
            }
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{mesh.name}_UniqueUV.asset");
        AssetDatabase.CreateAsset(uniqueMesh, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        mesh = savedMesh != null ? savedMesh : uniqueMesh;
        Texture2D uniqueTexture = CreateUniqueUvTextureAsset(folderPath, mesh.name, textureSize);
        if (uniqueTexture != null)
        {
            sourceTexture = uniqueTexture;
            ReloadWorkingTexture();
        }

        MarkUvViewDirty();
        ApplyPreviewTexture();
        statusMessage = uniqueTexture != null
            ? $"Created unique UV mesh and texture: {assetPath}"
            : $"Created unique UV mesh: {assetPath}";
        Selection.activeObject = mesh;
        Repaint();
    }

    private int ResolveUniqueUvTextureSize(int triangleCount)
    {
        int gridColumns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, triangleCount))));
        int requiredSize = gridColumns * UniqueUvMinCellPixels;
        if (workingTexture != null)
        {
            requiredSize = Mathf.Max(requiredSize, Mathf.Max(workingTexture.width, workingTexture.height));
        }
        else if (sourceTexture != null)
        {
            requiredSize = Mathf.Max(requiredSize, Mathf.Max(sourceTexture.width, sourceTexture.height));
        }

        int powerOfTwoSize = Mathf.NextPowerOfTwo(Mathf.Max(MinUniqueUvTextureSize, requiredSize));
        return Mathf.Clamp(powerOfTwoSize, MinUniqueUvTextureSize, MaxUniqueUvTextureSize);
    }

    private Texture2D CreateUniqueUvTextureAsset(string folderPath, string meshName, int textureSize)
    {
        string texturePath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{meshName}_Texture.png");
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] pixels = new Color32[textureSize * textureSize];
        Color32 baseColor = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = baseColor;
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        byte[] bytes = texture.EncodeToPNG();
        DestroyImmediate(texture);
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        string fullPath = Path.GetFullPath(texturePath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
        {
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        AssetDatabase.Refresh();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
    }

    private static Mesh BuildUniqueUvMesh(Mesh sourceMesh, int textureSize)
    {
        Vector3[] sourceVertices = sourceMesh.vertices;
        if (sourceVertices == null || sourceVertices.Length == 0)
        {
            throw new InvalidOperationException("Source mesh has no vertices.");
        }

        Vector3[] sourceNormals = ResolveSourceNormals(sourceMesh, sourceVertices);
        Vector4[] sourceTangents = sourceMesh.tangents;
        Color[] sourceColors = sourceMesh.colors;
        BoneWeight[] sourceBoneWeights = sourceMesh.boneWeights;
        bool copyNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
        bool copyTangents = sourceTangents != null && sourceTangents.Length == sourceVertices.Length;
        bool copyColors = sourceColors != null && sourceColors.Length == sourceVertices.Length;
        bool copyBoneWeights = sourceBoneWeights != null && sourceBoneWeights.Length == sourceVertices.Length;

        int subMeshCount = Mathf.Max(1, sourceMesh.subMeshCount);
        int totalTriangleCount = CountSourceTriangles(sourceMesh, subMeshCount);
        if (totalTriangleCount == 0)
        {
            throw new InvalidOperationException("Source mesh has no triangle submeshes.");
        }

        int gridColumns = Mathf.CeilToInt(Mathf.Sqrt(totalTriangleCount));
        int gridRows = Mathf.CeilToInt(totalTriangleCount / (float)gridColumns);
        float cellWidth = 1f / gridColumns;
        float cellHeight = 1f / gridRows;
        float insetU = ResolveUniqueUvCellInset(cellWidth, textureSize);
        float insetV = ResolveUniqueUvCellInset(cellHeight, textureSize);

        List<Vector3> vertices = new List<Vector3>(totalTriangleCount * 3);
        List<Vector2> uvs = new List<Vector2>(totalTriangleCount * 3);
        List<Vector3> normals = copyNormals ? new List<Vector3>(totalTriangleCount * 3) : null;
        List<Vector4> tangents = copyTangents ? new List<Vector4>(totalTriangleCount * 3) : null;
        List<Color> colors = copyColors ? new List<Color>(totalTriangleCount * 3) : null;
        List<BoneWeight> boneWeights = copyBoneWeights ? new List<BoneWeight>(totalTriangleCount * 3) : null;
        List<int>[] subMeshTriangles = new List<int>[subMeshCount];
        for (int i = 0; i < subMeshTriangles.Length; i++)
        {
            subMeshTriangles[i] = new List<int>();
        }

        int triangleIndex = 0;
        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                continue;
            }

            int[] sourceTriangles = sourceMesh.GetTriangles(subMeshIndex);
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                int i0 = sourceTriangles[i];
                int i1 = sourceTriangles[i + 1];
                int i2 = sourceTriangles[i + 2];
                if (!IsValidVertexIndex(i0, sourceVertices.Length)
                    || !IsValidVertexIndex(i1, sourceVertices.Length)
                    || !IsValidVertexIndex(i2, sourceVertices.Length))
                {
                    continue;
                }

                Vector2[] packedUvs = ResolvePackedTriangleUvs(
                    triangleIndex,
                    gridColumns,
                    cellWidth,
                    cellHeight,
                    insetU,
                    insetV,
                    sourceVertices[i0],
                    sourceVertices[i1],
                    sourceVertices[i2]);
                AddUniqueUvVertex(i0, packedUvs[0], sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceBoneWeights, vertices, uvs, normals, tangents, colors, boneWeights);
                AddUniqueUvVertex(i1, packedUvs[1], sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceBoneWeights, vertices, uvs, normals, tangents, colors, boneWeights);
                AddUniqueUvVertex(i2, packedUvs[2], sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceBoneWeights, vertices, uvs, normals, tangents, colors, boneWeights);

                int baseIndex = vertices.Count - 3;
                subMeshTriangles[subMeshIndex].Add(baseIndex);
                subMeshTriangles[subMeshIndex].Add(baseIndex + 1);
                subMeshTriangles[subMeshIndex].Add(baseIndex + 2);
                triangleIndex++;
            }
        }

        Mesh uniqueMesh = new Mesh
        {
            name = $"{sourceMesh.name}_UniqueUV",
            indexFormat = vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        uniqueMesh.SetVertices(vertices);
        uniqueMesh.SetUVs(0, uvs);
        if (normals != null)
        {
            uniqueMesh.SetNormals(normals);
        }
        else
        {
            uniqueMesh.RecalculateNormals();
        }

        if (tangents != null)
        {
            uniqueMesh.SetTangents(tangents);
        }

        if (colors != null)
        {
            uniqueMesh.SetColors(colors);
        }

        if (boneWeights != null)
        {
            uniqueMesh.boneWeights = boneWeights.ToArray();
            uniqueMesh.bindposes = sourceMesh.bindposes;
        }

        uniqueMesh.subMeshCount = subMeshCount;
        for (int i = 0; i < subMeshTriangles.Length; i++)
        {
            uniqueMesh.SetTriangles(subMeshTriangles[i], i);
        }

        uniqueMesh.RecalculateBounds();
        if (tangents == null)
        {
            uniqueMesh.RecalculateTangents();
        }

        return uniqueMesh;
    }

    private static int CountSourceTriangles(Mesh sourceMesh, int subMeshCount)
    {
        int count = 0;
        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                continue;
            }

            count += sourceMesh.GetTriangles(subMeshIndex).Length / 3;
        }

        return count;
    }

    private static Vector3[] ResolveSourceNormals(Mesh sourceMesh, Vector3[] sourceVertices)
    {
        Vector3[] normals = sourceMesh.normals;
        if (normals != null && normals.Length == sourceVertices.Length)
        {
            return normals;
        }

        Mesh normalMesh = Instantiate(sourceMesh);
        try
        {
            normalMesh.RecalculateNormals();
            normals = normalMesh.normals;
            return normals != null && normals.Length == sourceVertices.Length
                ? normals
                : null;
        }
        finally
        {
            DestroyImmediate(normalMesh);
        }
    }

    private static float ResolveUniqueUvCellInset(float cellSize, int textureSize)
    {
        float pixelInset = UniqueUvIslandPaddingPixels / (float)Mathf.Max(1, textureSize);
        float proportionalInset = cellSize * 0.08f;
        return Mathf.Min(Mathf.Max(pixelInset, proportionalInset), cellSize * 0.35f);
    }

    private static Vector2[] ResolvePackedTriangleUvs(
        int triangleIndex,
        int gridColumns,
        float cellWidth,
        float cellHeight,
        float insetU,
        float insetV,
        Vector3 vertex0,
        Vector3 vertex1,
        Vector3 vertex2)
    {
        int column = triangleIndex % gridColumns;
        int row = triangleIndex / gridColumns;
        float minU = (column * cellWidth) + insetU;
        float maxU = ((column + 1) * cellWidth) - insetU;
        float minV = (row * cellHeight) + insetV;
        float maxV = ((row + 1) * cellHeight) - insetV;
        Vector2[] localUvs = ProjectTriangleToLocalUv(vertex0, vertex1, vertex2);
        return FitTriangleUvsIntoCell(localUvs, minU, maxU, minV, maxV);
    }

    private static Vector2[] ProjectTriangleToLocalUv(Vector3 vertex0, Vector3 vertex1, Vector3 vertex2)
    {
        Vector3 edge01 = vertex1 - vertex0;
        Vector3 edge02 = vertex2 - vertex0;
        float length01 = edge01.magnitude;
        float length02 = edge02.magnitude;
        if (length01 <= 0.000001f || length02 <= 0.000001f)
        {
            return new[]
            {
                Vector2.zero,
                Vector2.right,
                Vector2.up
            };
        }

        Vector3 axisU = edge01 / length01;
        float projectedX = Vector3.Dot(edge02, axisU);
        float projectedYSqr = Mathf.Max(0f, edge02.sqrMagnitude - (projectedX * projectedX));
        float projectedY = Mathf.Sqrt(projectedYSqr);
        if (projectedY <= 0.000001f)
        {
            projectedY = length02 * 0.1f;
        }

        return new[]
        {
            Vector2.zero,
            new Vector2(length01, 0f),
            new Vector2(projectedX, projectedY)
        };
    }

    private static Vector2[] FitTriangleUvsIntoCell(Vector2[] localUvs, float minU, float maxU, float minV, float maxV)
    {
        float localMinX = Mathf.Min(localUvs[0].x, Mathf.Min(localUvs[1].x, localUvs[2].x));
        float localMaxX = Mathf.Max(localUvs[0].x, Mathf.Max(localUvs[1].x, localUvs[2].x));
        float localMinY = Mathf.Min(localUvs[0].y, Mathf.Min(localUvs[1].y, localUvs[2].y));
        float localMaxY = Mathf.Max(localUvs[0].y, Mathf.Max(localUvs[1].y, localUvs[2].y));
        float localWidth = Mathf.Max(0.000001f, localMaxX - localMinX);
        float localHeight = Mathf.Max(0.000001f, localMaxY - localMinY);
        float cellWidth = Mathf.Max(0.000001f, maxU - minU);
        float cellHeight = Mathf.Max(0.000001f, maxV - minV);
        float scale = Mathf.Min(cellWidth / localWidth, cellHeight / localHeight);
        float fittedWidth = localWidth * scale;
        float fittedHeight = localHeight * scale;
        float offsetU = minU + ((cellWidth - fittedWidth) * 0.5f);
        float offsetV = minV + ((cellHeight - fittedHeight) * 0.5f);

        Vector2[] result = new Vector2[3];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Vector2(
                offsetU + ((localUvs[i].x - localMinX) * scale),
                offsetV + ((localUvs[i].y - localMinY) * scale));
        }

        return result;
    }

    private static void AddUniqueUvVertex(
        int sourceIndex,
        Vector2 uv,
        Vector3[] sourceVertices,
        Vector3[] sourceNormals,
        Vector4[] sourceTangents,
        Color[] sourceColors,
        BoneWeight[] sourceBoneWeights,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Color> colors,
        List<BoneWeight> boneWeights)
    {
        vertices.Add(sourceVertices[sourceIndex]);
        uvs.Add(uv);
        normals?.Add(sourceNormals[sourceIndex]);
        tangents?.Add(sourceTangents[sourceIndex]);
        colors?.Add(sourceColors[sourceIndex]);
        boneWeights?.Add(sourceBoneWeights[sourceIndex]);
    }

    private static bool IsValidVertexIndex(int index, int vertexCount)
    {
        return index >= 0 && index < vertexCount;
    }

    private bool TryBuildPreviewRay(Rect previewRect, Vector2 mousePosition, out Ray ray)
    {
        ray = default;
        if (previewRenderer == null || previewRect.width <= 0f || previewRect.height <= 0f)
        {
            return false;
        }

        Vector2 localPosition = mousePosition - previewRect.position;
        float viewportX = Mathf.Clamp01(localPosition.x / previewRect.width);
        float viewportY = Mathf.Clamp01(1f - (localPosition.y / previewRect.height));
        ray = previewRenderer.camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
        return true;
    }

    private static bool TryRaycastMesh(Mesh targetMesh, Ray ray, out MeshRaycastHit hit)
    {
        hit = default;
        if (!TryReadMeshPaintData(targetMesh, out Vector3[] vertices, out Vector2[] uvs, out int[] triangles))
        {
            return false;
        }

        float closestDistance = float.MaxValue;
        MeshRaycastHit closestHit = default;
        bool hasHit = false;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0
                || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
            {
                continue;
            }

            if (!IntersectRayTriangle(
                    ray,
                    vertices[i0],
                    vertices[i1],
                    vertices[i2],
                    out float distance,
                    out float barycentricU,
                    out float barycentricV))
            {
                continue;
            }

            if (distance >= closestDistance)
            {
                continue;
            }

            float barycentricW = 1f - barycentricU - barycentricV;
            Vector3 point = (vertices[i0] * barycentricW) + (vertices[i1] * barycentricU) + (vertices[i2] * barycentricV);
            closestHit = new MeshRaycastHit(
                point,
                vertices[i0],
                vertices[i1],
                vertices[i2],
                uvs[i0],
                uvs[i1],
                uvs[i2]);
            closestDistance = distance;
            hasHit = true;
        }

        hit = closestHit;
        return hasHit;
    }

    private static bool TryReadMeshPaintData(
        Mesh targetMesh,
        out Vector3[] vertices,
        out Vector2[] uvs,
        out int[] triangles)
    {
        vertices = null;
        uvs = null;
        triangles = null;
        if (targetMesh == null)
        {
            return false;
        }

        try
        {
            vertices = targetMesh.vertices;
            uvs = targetMesh.uv;
            triangles = targetMesh.triangles;
        }
        catch (Exception)
        {
            return false;
        }

        return vertices != null
            && uvs != null
            && triangles != null
            && vertices.Length > 0
            && uvs.Length >= vertices.Length
            && triangles.Length >= 3;
    }

    private static bool IntersectRayTriangle(
        Ray ray,
        Vector3 vertex0,
        Vector3 vertex1,
        Vector3 vertex2,
        out float distance,
        out float barycentricU,
        out float barycentricV)
    {
        const float epsilon = 0.000001f;
        distance = 0f;
        barycentricU = 0f;
        barycentricV = 0f;

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
        barycentricU = Vector3.Dot(t, p) * inverseDeterminant;
        if (barycentricU < 0f || barycentricU > 1f)
        {
            return false;
        }

        Vector3 q = Vector3.Cross(t, edge1);
        barycentricV = Vector3.Dot(ray.direction, q) * inverseDeterminant;
        if (barycentricV < 0f || barycentricU + barycentricV > 1f)
        {
            return false;
        }

        distance = Vector3.Dot(edge2, q) * inverseDeterminant;
        return distance > epsilon;
    }

    private bool PaintTextureAtScreenBrush(Rect previewRect, Vector2 mousePosition)
    {
        if (workingTexture == null || previewRenderer == null || previewRenderer.camera == null)
        {
            return false;
        }

        if (!TryReadMeshPaintData(mesh, out Vector3[] vertices, out Vector2[] uvs, out int[] triangles))
        {
            statusMessage = "Brush paint requires a readable mesh with UVs.";
            return false;
        }

        int width = workingTexture.width;
        int height = workingTexture.height;
        Color32[] pixels = workingTexture.GetPixels32();
        float radius = Mathf.Clamp(brushRadius, MinBrushRadius, MaxBrushRadius);
        float innerRadius = radius * Mathf.Clamp01(brushHardness);
        Rect brushBounds = Rect.MinMaxRect(
            mousePosition.x - radius,
            mousePosition.y - radius,
            mousePosition.x + radius,
            mousePosition.y + radius);
        bool changed = false;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (!IsValidVertexIndex(i0, vertices.Length)
                || !IsValidVertexIndex(i1, vertices.Length)
                || !IsValidVertexIndex(i2, vertices.Length))
            {
                continue;
            }

            if (PaintScreenBrushOnTriangle(
                    pixels,
                    width,
                    height,
                    vertices[i0],
                    vertices[i1],
                    vertices[i2],
                    NormalizeUv01(uvs[i0]),
                    NormalizeUv01(uvs[i1]),
                    NormalizeUv01(uvs[i2]),
                    previewRect,
                    mousePosition,
                    brushBounds,
                    radius,
                    innerRadius,
                    brushColor))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            statusMessage = "Brush made no visible change.";
            return false;
        }

        workingTexture.SetPixels32(pixels);
        workingTexture.Apply(false, false);
        textureDirty = true;
        MarkUvViewDirty();
        ApplyPreviewTexture();
        statusMessage = "Painted screen brush stamp.";
        return true;
    }

    private bool PaintScreenBrushOnTriangle(
        Color32[] pixels,
        int width,
        int height,
        Vector3 vertex0,
        Vector3 vertex1,
        Vector3 vertex2,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Rect previewRect,
        Vector2 mousePosition,
        Rect brushBounds,
        float radius,
        float innerRadius,
        Color targetColor)
    {
        if (!TryBuildProjectedTriangleBounds(previewRect, vertex0, vertex1, vertex2, out Rect screenBounds)
            || !brushBounds.Overlaps(screenBounds))
        {
            return false;
        }

        Vector2 p0 = UvToTexturePoint(uv0, width, height);
        Vector2 p1 = UvToTexturePoint(uv1, width, height);
        Vector2 p2 = UvToTexturePoint(uv2, width, height);
        float area = EdgeFunction(p0, p1, p2);
        if (Mathf.Abs(area) < 0.000001f)
        {
            return false;
        }

        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, height - 1);
        float radiusSqr = radius * radius;
        bool changed = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 texturePoint = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = EdgeFunction(p1, p2, texturePoint) / area;
                float w1 = EdgeFunction(p2, p0, texturePoint) / area;
                float w2 = EdgeFunction(p0, p1, texturePoint) / area;
                if (w0 < -0.0001f || w1 < -0.0001f || w2 < -0.0001f)
                {
                    continue;
                }

                Vector3 worldPoint = (vertex0 * w0) + (vertex1 * w1) + (vertex2 * w2);
                if (!TryWorldToPreviewGuiPoint(previewRect, worldPoint, out Vector2 guiPoint))
                {
                    continue;
                }

                float distanceSqr = (guiPoint - mousePosition).sqrMagnitude;
                if (distanceSqr > radiusSqr)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSqr);
                float falloff = distance <= innerRadius
                    ? 1f
                    : 1f - Mathf.InverseLerp(innerRadius, radius, distance);
                float strength = Mathf.Clamp01(brushOpacity) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(falloff));
                if (strength <= 0f)
                {
                    continue;
                }

                int pixelIndex = (y * width) + x;
                Color32 next = (Color32)Color.Lerp(pixels[pixelIndex], targetColor, strength);
                if (pixels[pixelIndex].Equals(next))
                {
                    continue;
                }

                pixels[pixelIndex] = next;
                changed = true;
            }
        }

        return changed;
    }

    private bool TryBuildProjectedTriangleBounds(
        Rect previewRect,
        Vector3 vertex0,
        Vector3 vertex1,
        Vector3 vertex2,
        out Rect bounds)
    {
        bounds = default;
        if (!TryWorldToPreviewGuiPoint(previewRect, vertex0, out Vector2 p0)
            || !TryWorldToPreviewGuiPoint(previewRect, vertex1, out Vector2 p1)
            || !TryWorldToPreviewGuiPoint(previewRect, vertex2, out Vector2 p2))
        {
            return false;
        }

        bounds = Rect.MinMaxRect(
            Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - 1f,
            Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) - 1f,
            Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) + 1f,
            Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) + 1f);
        return true;
    }

    private void FinishPaintStroke()
    {
        if (paintStrokeUndoCaptured && !paintStrokeChanged && undoHistory.Count > 0)
        {
            undoHistory.RemoveAt(undoHistory.Count - 1);
        }

        paintStrokeUndoCaptured = false;
        paintStrokeChanged = false;
    }

    private void RecordUndoState()
    {
        TextureSnapshot snapshot = CaptureWorkingTexture();
        if (snapshot.Pixels == null || snapshot.Pixels.Length == 0)
        {
            return;
        }

        PushSnapshot(undoHistory, snapshot);
    }

    private TextureSnapshot CaptureWorkingTexture()
    {
        if (workingTexture == null)
        {
            return default;
        }

        return new TextureSnapshot(workingTexture.width, workingTexture.height, workingTexture.GetPixels32());
    }

    private static void PushSnapshot(List<TextureSnapshot> history, TextureSnapshot snapshot)
    {
        history.Add(snapshot);
        if (history.Count > MaxHistorySteps)
        {
            history.RemoveAt(0);
        }
    }

    private static TextureSnapshot PopSnapshot(List<TextureSnapshot> history)
    {
        int index = history.Count - 1;
        TextureSnapshot snapshot = history[index];
        history.RemoveAt(index);
        return snapshot;
    }

    private void UndoPaint()
    {
        if (!CanUndo)
        {
            return;
        }

        PushSnapshot(redoHistory, CaptureWorkingTexture());
        RestoreWorkingTexture(PopSnapshot(undoHistory));
        statusMessage = $"Undo. Undo: {undoHistory.Count}, Redo: {redoHistory.Count}";
    }

    private void RedoPaint()
    {
        if (!CanRedo)
        {
            return;
        }

        PushSnapshot(undoHistory, CaptureWorkingTexture());
        RestoreWorkingTexture(PopSnapshot(redoHistory));
        statusMessage = $"Redo. Undo: {undoHistory.Count}, Redo: {redoHistory.Count}";
    }

    private void RestoreWorkingTexture(TextureSnapshot snapshot)
    {
        if (workingTexture == null || snapshot.Pixels == null || snapshot.Pixels.Length == 0)
        {
            return;
        }

        if (snapshot.Width != workingTexture.width || snapshot.Height != workingTexture.height)
        {
            statusMessage = "Cannot restore history because the texture size changed.";
            return;
        }

        workingTexture.SetPixels32(snapshot.Pixels);
        workingTexture.Apply(false, false);
        textureDirty = true;
        MarkUvViewDirty();
        ApplyPreviewTexture();
        Repaint();
    }

    private void ClearHistory()
    {
        undoHistory.Clear();
        redoHistory.Clear();
        paintStrokeUndoCaptured = false;
        paintStrokeChanged = false;
    }

    private void LoadSelection()
    {
        Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            statusMessage = "Nothing selected.";
            return;
        }

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            switch (selectedObjects[i])
            {
                case Mesh selectedMesh:
                    if (mesh != selectedMesh)
                    {
                        mesh = selectedMesh;
                        MarkUvViewDirty();
                    }

                    break;
                case Texture2D selectedTexture:
                    sourceTexture = selectedTexture;
                    ReloadWorkingTexture();
                    break;
                case GameObject gameObject:
                    LoadFromGameObject(gameObject);
                    break;
            }
        }

        statusMessage = "Loaded selected assets.";
        Repaint();
    }

    private void LoadFromGameObject(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return;
        }

        MeshFilter meshFilter = gameObject.GetComponentInChildren<MeshFilter>(true);
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            if (mesh != meshFilter.sharedMesh)
            {
                mesh = meshFilter.sharedMesh;
                MarkUvViewDirty();
            }
        }

        MeshRenderer meshRenderer = gameObject.GetComponentInChildren<MeshRenderer>(true);
        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            return;
        }

        Texture texture = null;
        Material material = meshRenderer.sharedMaterial;
        if (material.HasProperty(BaseMapPropertyId))
        {
            texture = material.GetTexture(BaseMapPropertyId);
        }

        if (texture == null && material.HasProperty(MainTexPropertyId))
        {
            texture = material.GetTexture(MainTexPropertyId);
        }

        if (texture is Texture2D texture2D)
        {
            sourceTexture = texture2D;
            ReloadWorkingTexture();
        }
    }

    private void ReloadWorkingTexture()
    {
        DestroyWorkingTexture();
        ClearHistory();
        MarkUvViewDirty();
        if (sourceTexture == null)
        {
            textureDirty = false;
            statusMessage = "No texture loaded.";
            return;
        }

        workingTexture = CreateReadableTextureCopy(sourceTexture);
        if (workingTexture == null)
        {
            statusMessage = "Failed to copy texture.";
            return;
        }

        textureDirty = false;
        MarkUvViewDirty();
        ApplyPreviewTexture();
        statusMessage = $"Loaded texture copy ({workingTexture.width}x{workingTexture.height}).";
    }

    private static Texture2D CreateReadableTextureCopy(Texture2D source)
    {
        if (source == null || source.width <= 0 || source.height <= 0)
        {
            return null;
        }

        RenderTexture renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previousRenderTexture = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                name = $"{source.name}_PaintWorkingCopy",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode,
                wrapMode = source.wrapMode
            };
            copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            copy.Apply(false, false);
            return copy;
        }
        catch
        {
            if (copy != null)
            {
                DestroyImmediate(copy);
            }

            return null;
        }
        finally
        {
            RenderTexture.active = previousRenderTexture;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private void DestroyWorkingTexture()
    {
        MarkUvViewDirty();
        ClearHistory();
        if (workingTexture == null)
        {
            return;
        }

        DestroyImmediate(workingTexture);
        workingTexture = null;
    }

    private void SaveWorkingTexture()
    {
        if (workingTexture == null)
        {
            return;
        }

        string assetPath = sourceTexture != null ? AssetDatabase.GetAssetPath(sourceTexture) : string.Empty;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            SaveWorkingTextureAsPng();
            return;
        }

        string extension = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? string.Empty;
        byte[] bytes = extension switch
        {
            ".png" => workingTexture.EncodeToPNG(),
            ".jpg" => workingTexture.EncodeToJPG(95),
            ".jpeg" => workingTexture.EncodeToJPG(95),
            ".tga" => workingTexture.EncodeToTGA(),
            _ => null
        };

        if (bytes == null || bytes.Length == 0)
        {
            SaveWorkingTextureAsPng();
            return;
        }

        WriteTextureBytes(assetPath, bytes);
        textureDirty = false;
        statusMessage = $"Saved texture: {assetPath}";
    }

    private void SaveWorkingTextureAsPng()
    {
        if (workingTexture == null)
        {
            return;
        }

        string sourcePath = sourceTexture != null ? AssetDatabase.GetAssetPath(sourceTexture) : string.Empty;
        string defaultName = !string.IsNullOrWhiteSpace(sourcePath)
            ? $"{Path.GetFileNameWithoutExtension(sourcePath)}_Painted"
            : "PaintedTexture";
        string defaultDirectory = !string.IsNullOrWhiteSpace(sourcePath)
            ? Path.GetDirectoryName(sourcePath)?.Replace("\\", "/")
            : "Assets";

        string assetPath = EditorUtility.SaveFilePanelInProject(
            "Save Painted Texture",
            defaultName,
            "png",
            "Choose where to save the painted texture.",
            string.IsNullOrWhiteSpace(defaultDirectory) ? "Assets" : defaultDirectory);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        byte[] bytes = workingTexture.EncodeToPNG();
        WriteTextureBytes(assetPath, bytes);
        Texture2D savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (savedTexture != null)
        {
            sourceTexture = savedTexture;
        }

        textureDirty = false;
        statusMessage = $"Saved texture: {assetPath}";
    }

    private static void WriteTextureBytes(string assetPath, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || bytes == null || bytes.Length == 0)
        {
            return;
        }

        string fullPath = Path.GetFullPath(assetPath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
    }
}
