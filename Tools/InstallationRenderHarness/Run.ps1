$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-InstallationRender-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
function Member([string]$relative, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $scripts $relative))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$installation = 'Object/MapObj/InstallationObject/InstallationObject.cs'
$generated = "using UnityEngine; using ProjectF.MapObjects; public partial class InstallationObject {`n"
foreach ($signature in @('public void ConfigurePlacementRuntime(', 'internal void BindRuntimeMapObjectHandle(')) {
    $generated += (Member $installation $signature) + "`n"
}
$generated += "} public partial class Vehicle {`n" + (Member 'Object/MapObj/InstallationObject/Vehicle/Vehicle.cs' 'protected bool RefreshSingleCellRuntimePlacement(')
$generated += "} public partial class Train {`n" + (Member 'Object/MapObj/InstallationObject/Vehicle/Train.cs' 'protected void RefreshRuntimeCoordinate(')
$generated += "} public partial class Handcart {`n" + (Member 'Object/MapObj/InstallationObject/Vehicle/Handcart.cs' 'private void RefreshRuntimePlacement(')
$generated += "} public partial class TerrainGenerator {`n" + (Member 'Map/TerrainGenerator.cs' 'public void RefreshMovedInstallationRuntimeState(') + "}`n"
[IO.File]::WriteAllText((Join-Path $probe 'VehicleMovement.cs'), $generated)
$files = @((Join-Path $PSScriptRoot 'Checks.cs'), (Join-Path $PSScriptRoot 'UnityBoundaries.cs'),
    (Join-Path $PSScriptRoot 'VehicleMovementChecks.cs'), (Join-Path $PSScriptRoot 'RenderCacheChecks.cs'), (Join-Path $probe 'VehicleMovement.cs'),
    (Join-Path $scripts 'Rendering/InstallationBatchRenderer.cs'),
    (Join-Path $scripts 'Rendering/InstallationMaterialVariants.cs'),
    (Join-Path $scripts 'Rendering/SpriteMeshCache.cs'),
    (Join-Path $scripts 'Rendering/InstallationRigidAnimationTemplate.cs'),
    (Join-Path $scripts 'MapObjects/MapObjectHandle.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
