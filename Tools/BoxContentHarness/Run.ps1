param([switch]$Baseline)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Box/BoxObject.cs'
$sourceText = if ($Baseline) { (git -C $repo show "HEAD:$sourcePath") -join "`n" } else { [IO.File]::ReadAllText((Join-Path $repo $sourcePath)) }
function Member([string]$signature) {
    $start = $sourceText.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $sourceText.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($sourceText[$end] -eq '{') { $depth++ }
        if ($sourceText[$end] -eq '}') { $depth-- }
        $end++
    }
    return $sourceText.Substring($start, $end - $start)
}
$source = "using System; using System.Collections.Generic; using UnityEngine; public partial class BoxObject { public const int DefaultMinimumRetainedItemCount = 0; private int minimumRetainedItemCount; public int MinimumRetainedItemCount => Mathf.Max(0, minimumRetainedItemCount);`n"
foreach ($signature in @('private bool TryGetContentBlock(', 'public bool CanPutContainedObjects(', 'public bool TryPutOneContainedObjectInstant(', 'public int GetExtractableContainedItemCount(', 'public bool CanTakeContainedObject(', 'public int GetMinimumRetainedItemCountLimit(', 'public void SetMinimumRetainedItemCount(', 'private bool TryResolveMinimumRetainedItemCountLimit(', 'public bool TryTakeOneContainedObject(System.Predicate<int>')) {
    $source += (Member $signature) + "`n"
}
$source += '}'
$sourceText = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs'))
$source += ' public partial class InputOutputModule { ' + (Member 'public static bool CanAddItemToRuntimeIoOverlapCoordinate(') + ' }'
$sourceText = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/Block.cs'))
$source += ' public partial class Block { ' + (Member 'public bool CanAddInputAreaCenterObjects(int count, int itemId)') + ' }'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BoxContent-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
