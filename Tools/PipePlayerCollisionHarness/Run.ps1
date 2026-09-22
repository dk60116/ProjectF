$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $root 'FactorioProject/Assets/Scripts/Map/PipeWorld.cs'))
function Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$generated = "using UnityEngine; using ProjectF.Conveyors; public partial class PipeRuntimeRecord {`n"
$generated += (Member 'private readonly struct PlayerCollisionPart') + "`n"
$generated += (Member 'internal bool TrySweepPlayer(') + "`n}`n"
$generated += Member 'internal static class PipePlayerCollision'
$source = [IO.File]::ReadAllText((Join-Path $root 'FactorioProject/Assets/Scripts/Map/ConveyorWorld.cs'))
$generated += "public partial class ConveyorRuntimeRecord {`n" + (Member 'internal bool TrySweepCornerPlayer(') + "`n}"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-PipeCollision-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PrefabChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $root 'FactorioProject/Assets/Scripts/Character/Player/PlayerController.ConveyorCollision.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $root 'FactorioProject/Assets/Scripts/Map/ConveyorSideBarrier.cs') -Destination $probe
[xml]$unityProject = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/Assembly-CSharp.csproj')
$core = ($unityProject.Project.ItemGroup.Reference | Where-Object { $_.Include -eq 'UnityEngine.CoreModule' }).HintPath
$physics = Join-Path ([IO.Path]::GetDirectoryName($core)) 'UnityEngine.PhysicsModule.dll'
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $core + '</HintPath></Reference><Reference Include="UnityEngine.PhysicsModule"><HintPath>' + $physics + '</HintPath></Reference></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), $project)
dotnet run --project (Join-Path $probe 'Probe.csproj') --configuration Release -- $root
exit $LASTEXITCODE
