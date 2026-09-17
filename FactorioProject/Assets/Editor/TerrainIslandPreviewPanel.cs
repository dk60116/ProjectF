using System;
using UnityEditor;
using UnityEngine;

internal sealed class TerrainIslandPreviewPanel : IDisposable
{
    private const int MaxPreviewResolution = 256;
    private const double UpdateBudgetSeconds = 0.008;

    private readonly Action repaint;
    private Texture2D texture;
    private Color32[] pixels;
    private TerrainGenerator generator;
    private int terrainVersion = int.MinValue;
    private int mapSize;
    private int resolution;
    private int nextRow;
    private bool isGenerating;

    public TerrainIslandPreviewPanel(Action repaint)
    {
        this.repaint = repaint;
        EditorApplication.update += Process;
    }

    public void Draw(TerrainGenerator target, float windowWidth)
    {
        EnsureGeneration(target);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Island Preview", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(64f)))
        {
            RequestGeneration(target);
        }

        EditorGUILayout.EndHorizontal();

        int targetMapSize = target != null ? target.CurrentMapSize : 0;
        string previewInfo = target == null
            ? "No TerrainGenerator"
            : isGenerating
                ? $"Map {targetMapSize} x {targetMapSize}  |  Building {Mathf.Min(nextRow, resolution)} / {resolution}"
                : $"Map {targetMapSize} x {targetMapSize}  |  Seed {target.CurrentSeed}";
        EditorGUILayout.LabelField(previewInfo, EditorStyles.miniLabel);

        float availableWidth = Mathf.Max(160f, windowWidth - 42f);
        float previewSize = Mathf.Min(480f, availableWidth);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        Rect previewRect = GUILayoutUtility.GetRect(
            previewSize,
            previewSize,
            GUILayout.Width(previewSize),
            GUILayout.Height(previewSize));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUI.DrawRect(previewRect, new Color(0.07f, 0.07f, 0.07f));
        if (texture != null)
        {
            GUI.DrawTexture(previewRect, texture, ScaleMode.ScaleToFit, false);
        }
        else
        {
            GUI.Label(previewRect, "Building island preview...", EditorStyles.centeredGreyMiniLabel);
        }

        GUI.Box(previewRect, GUIContent.none);
        if (isGenerating && resolution > 0)
        {
            float progress = Mathf.Clamp01(nextRow / (float)resolution);
            Rect progressRect = new Rect(previewRect.x + 8f, previewRect.yMax - 22f, previewRect.width - 16f, 14f);
            EditorGUI.ProgressBar(progressRect, progress, $"{progress * 100f:0}%");
        }

        EditorGUILayout.HelpBox(
            "전체 맵을 실제 지형 바이옴 계산으로 미리봅니다. 설정이나 시드가 바뀌면 자동으로 갱신됩니다.",
            MessageType.None);
        EditorGUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    public void RequestGeneration(TerrainGenerator target)
    {
        if (target == null)
        {
            isGenerating = false;
            pixels = null;
            return;
        }

        bool generatorChanged = generator != target;
        if (generatorChanged && texture != null)
        {
            UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }

        generator = target;
        terrainVersion = target.TerrainGenerationVersion;
        mapSize = target.CurrentMapSize;
        resolution = Mathf.Clamp(mapSize, 32, MaxPreviewResolution);
        int pixelCount = resolution * resolution;
        if (pixels == null || pixels.Length != pixelCount)
        {
            pixels = new Color32[pixelCount];
        }

        nextRow = 0;
        isGenerating = true;
        repaint?.Invoke();
    }

    public void Dispose()
    {
        EditorApplication.update -= Process;
        isGenerating = false;
        pixels = null;
        generator = null;
        if (texture == null)
        {
            return;
        }

        UnityEngine.Object.DestroyImmediate(texture);
        texture = null;
    }

    private void EnsureGeneration(TerrainGenerator target)
    {
        if (target == null)
        {
            return;
        }

        if (generator != target
            || terrainVersion != target.TerrainGenerationVersion
            || mapSize != target.CurrentMapSize)
        {
            RequestGeneration(target);
        }
    }

    private void Process()
    {
        if (!isGenerating || generator == null || pixels == null)
        {
            return;
        }

        if (generator.TerrainGenerationVersion != terrainVersion || generator.CurrentMapSize != mapSize)
        {
            RequestGeneration(generator);
            return;
        }

        double deadline = EditorApplication.timeSinceStartup + UpdateBudgetSeconds;
        do
        {
            SampleRow(nextRow);
            nextRow++;
        }
        while (nextRow < resolution && EditorApplication.timeSinceStartup < deadline);

        if (nextRow >= resolution)
        {
            ApplyTexture();
        }

        repaint?.Invoke();
    }

    private void SampleRow(int previewY)
    {
        int mapMinCoordinate = -(mapSize / 2);
        int worldY = mapMinCoordinate + ((((previewY * 2) + 1) * mapSize) / (resolution * 2));
        int pixelOffset = previewY * resolution;
        for (int previewX = 0; previewX < resolution; previewX++)
        {
            int worldX = mapMinCoordinate + ((((previewX * 2) + 1) * mapSize) / (resolution * 2));
            pixels[pixelOffset + previewX] = generator.GetMapBiomeColor32At(new Vector2Int(worldX, worldY));
        }
    }

    private void ApplyTexture()
    {
        if (texture == null || texture.width != resolution || texture.height != resolution)
        {
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "Terrain Island Preview",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        isGenerating = false;
    }
}
