from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2] / 'FactorioProject/Assets/Scripts'
out = Path(sys.argv[1])
runtime = (root / 'Map/UtilityPoleRuntime.cs').read_text(encoding='utf-8-sig')
identity = (root / 'Map/UtilityPoleRuntime.Identity.cs').read_text(encoding='utf-8-sig')


def member(source, signature):
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def expression(signature):
    start = identity.index(signature)
    return identity[start:identity.index(';', start) + 1]


methods = [
    'public static void RegisterBlueprintPreview(',
    'public static void UnregisterBlueprintPreview(',
    'public static void ClearBlueprintPreviews()',
    'private static void BuildVisualPoleScratch()',
    'private static void CleanupPreviewPoleRuntimes()',
    'private static bool IsValidPreviewPole(',
    'private static bool HasTopologyReplacementPreview()',
    'private static void MarkPreviewPoleConnectionsDirty()',
    'private void ResolveLinePointReferences()',
    'private void EnsureUtilityPoleWires()',
    'private void RefreshUtilityPoleWires()',
    'private void RefreshInternalWire(',
    'private void RefreshUtilityPoleWire(\n        UtilityPoleWire wire,',
    'private void RenderConnectionLine(UtilityPoleLinePoint startPoint, Vector3 endPosition)',
    'private float ResolveConnectionLineSagDepth(Vector3 startPosition, Vector3 endPosition)',
    'private UtilityPoleWire EnsureConnectionUtilityPoleWire(',
    'private void HideConnectionUtilityPoleWires()',
    'private void ResetLinePointConnections()',
    'private static void SetUtilityPoleWireVisible(',
    'private readonly struct PreviewPoleRuntime',
]
code = '\n'.join(member(runtime, signature) for signature in methods)
properties = '\n'.join(expression(signature) for signature in [
    'public bool IsRuntimeActive =>', 'internal bool IsPreviewPresentationActive =>',
    'public Vector3 WorldPosition =>', 'public Quaternion WorldRotation =>',
])
code += '\nprivate static Vector3 ResolveLinePointWorldPosition(UtilityPoleLinePoint point) => point != null ? point.Position : Vector3.zero;'
point = member(identity, 'internal sealed class UtilityPoleLinePoint')
(out / 'PreviewRuntime.cs').write_text(
    'using System.Collections.Generic; using UnityEngine; using ProjectF.Rendering; '
    'namespace ProjectF.Power { partial class UtilityPoleRuntime { '
    + code + '\n' + properties + ' } ' + point + ' }', encoding='utf-8')
wire_source = (root / 'Rendering/UtilityPoleWireRenderer.cs').read_text(encoding='utf-8-sig')
(out / 'Wire.cs').write_text('using UnityEngine; namespace ProjectF.Rendering { '
    + member(wire_source, 'internal sealed class UtilityPoleWire') + ' }', encoding='utf-8')
