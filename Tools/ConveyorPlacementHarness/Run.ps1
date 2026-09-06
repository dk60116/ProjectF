param([switch]$BeforeFix)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$file = 'FactorioProject/Assets/Scripts/Map/Block.cs'
$source = if ($BeforeFix) { (git -C $repo show "HEAD:$file") -join "`n" } else { [IO.File]::ReadAllText((Join-Path $repo $file)) }
function Read-Member([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$generated = "using System; using UnityEngine; public partial class Block {`n"
foreach ($signature in @('private bool TryAddConveyorObjectAnimatedWithPlacementReference(',
    'private bool IsConveyorLaneMovementHeld(', 'private void HoldConveyorLaneMovement(',
    'private void ClearConveyorLaneMovementHold(')) { $generated += (Read-Member $signature) + "`n" }
if (!$BeforeFix) { $generated += (Read-Member 'private void CompleteConveyorItemPlacement(') + "`n" }
$generated += '}'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ConveyorPlacement-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
Set-Content -LiteralPath (Join-Path $temp 'Production.cs') -Value $generated
$checks = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $temp 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="' + $checks + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $temp 'Probe.csproj')
exit $LASTEXITCODE
