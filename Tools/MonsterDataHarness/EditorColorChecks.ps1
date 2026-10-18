param([string]$Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')))

$ErrorActionPreference = 'Stop'
$window = Get-Content -LiteralPath (Join-Path $Root 'FactorioProject/Assets/Editor/Monsters/MonsterDataEditorWindow.cs') -Raw
$utility = Get-Content -LiteralPath (Join-Path $Root 'FactorioProject/Assets/Editor/AnimalDataEditorUtility.cs') -Raw
function ExtractMethod([string]$source, [string]$signature, [int]$indent) {
    $pattern = '(?ms)^' + (' ' * $indent) + [regex]::Escape($signature) + '\s*\{.*?^' + (' ' * $indent) + '\}'
    $match = [regex]::Match($source, $pattern)
    if (-not $match.Success) { throw "Missing production method: $signature" }
    return $match.Value
}
$onGui = ExtractMethod $window 'private void OnGUI()' 8
$styles = ExtractMethod $window 'private void EnsureStyles()' 8
$colorSetter = ExtractMethod $utility 'public static void SetGuiStyleTextColor(GUIStyle style, Color color)' 4

# Exercise the production draw boundary and styles without starting Unity or a graphics context.
Add-Type -TypeDefinition @"
using System;
namespace ProjectF.MonsterEditorColorHarness
{
    struct Color
    {
        public int Value;
        public static Color white => new Color { Value = 1 };
    }
    enum TextAnchor { MiddleLeft }
    enum TextClipping { Clip }
    sealed class GUIStyleState { public Color textColor; }
    sealed class GUIStyle
    {
        public static int Created;
        public TextAnchor alignment;
        public TextClipping clipping;
        public bool wordWrap;
        public GUIStyleState normal = new GUIStyleState(), hover = new GUIStyleState(),
            active = new GUIStyleState(), focused = new GUIStyleState(),
            onNormal = new GUIStyleState(), onHover = new GUIStyleState(),
            onActive = new GUIStyleState(), onFocused = new GUIStyleState();
        public GUIStyle(GUIStyle source) { Created++; }
    }
    static class EditorStyles
    {
        public static GUIStyle label = new GUIStyle(null), boldLabel = new GUIStyle(null);
    }
    static class GUI { public static Color color, contentColor; }
    static class ProjectFEditorGUIUtility
    {
        $colorSetter
    }
    public sealed class Probe
    {
        private GUIStyle rowStyle, headerStyle;
        private bool throwOnDraw;
        private int checks;
        $onGui
        $styles
        private void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        private void DrawWindowContent()
        {
            Require(GUI.color.Value == 1 && GUI.contentColor.Value == 1, "Draw inherits no black tint");
            EnsureStyles();
            foreach (GUIStyle style in new[] { rowStyle, headerStyle })
                foreach (GUIStyleState state in new[] { style.normal, style.hover, style.active,
                    style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
                    Require(state.textColor.Value == 1, "Every name label state is white");
            if (throwOnDraw) throw new OperationCanceledException();
        }
        public static int Run()
        {
            var probe = new Probe();
            foreach (int tint in new[] { 0, 1, 7 })
            {
                GUI.color = new Color { Value = tint };
                GUI.contentColor = new Color { Value = 8 - tint };
                probe.OnGUI();
                probe.Require(GUI.color.Value == tint && GUI.contentColor.Value == 8 - tint,
                    "Original GUI colors restored after draw");
            }
            int created = GUIStyle.Created;
            probe.OnGUI();
            probe.Require(GUIStyle.Created == created, "Styles do not allocate on repeated repaint");
            probe.throwOnDraw = true;
            try { probe.OnGUI(); }
            catch (OperationCanceledException) { }
            probe.Require(GUI.color.Value == 7 && GUI.contentColor.Value == 1,
                "Original GUI colors restored when draw exits exceptionally");
            return probe.checks;
        }
    }
}
"@
$checks = [ProjectF.MonsterEditorColorHarness.Probe]::Run()
Write-Host "PASS: $checks production Monster editor GUI color checks"
