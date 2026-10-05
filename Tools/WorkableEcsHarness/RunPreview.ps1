$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-WorkablePreview-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/WorkableObject.cs'))
function Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$start = $source.IndexOf('    internal static readonly WorkableRangeIndex RangeIndex')
$end = $source.IndexOf('    [SerializeField', $start)
$generated = "using System.Collections.Generic; using UnityEngine; using ProjectF.MapObjects; public static partial class WorkableVisualProbe {`n" + $source.Substring($start, $end - $start)
foreach ($signature in @('internal static void RegisterTarget(', 'internal static void UnregisterTarget(',
    'internal static void SetTargetSelected(', 'public static void SetCraftingSlotRangeVisualsRequested(',
    'public static void SetInstallOrEditWorkableSelectionRangeVisualsRequested(',
    'internal static void UpdateRangeVisual(', 'private static void AppendRange(', 'private static bool ShouldShowWorkableRangeVisuals(')) {
    $generated += (Member $signature).Replace('Application.isPlaying', 'PreviewEngine.IsPlaying') + "`n"
}
$generated += "}`n" + (Member 'public readonly struct WorkableObjectRangeVisualRequest')
[IO.File]::WriteAllText((Join-Path $probe 'VisualMembers.cs'), $generated)
foreach ($relative in @('Map/IWorkableTarget.cs', 'Map/WorkableRangeIndex.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)) -Destination $probe
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PreviewChecks.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0169;0414</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
