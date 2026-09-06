$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$base = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/'
$generated = "using System.Collections.Generic;`npublic partial class Sprinkler {`n"
foreach ($signature in @(
    'public override void ManagedUpdateTick(',
    'private void PerformSpray(',
    'private int CollectWateringTargets()',
    'private static bool TryGetWateringTarget(',
    'public void GetWaterStorageInfo(',
    'private static bool IsConnectedWaterTank(',
    'private static float GetUsableWaterLiters(',
    'private bool TryConsumeSprayWater(',
    'protected override bool ShouldAutoPullFluidFromConnectedStorage()',
    'protected override string ResolveObjectInfoStatus(')) {
    $generated += (Read-Member ($base + 'Sprinkler.cs') $signature) + "`n"
}
$generated += (Select-String -LiteralPath (Join-Path $repo ($base + 'Sprinkler.cs')) -Pattern '^    protected override bool UsesConnectedTankNetworkStorage =>').Line + "`n"
$generated += "}`npublic partial class InstallationObject {`n"
foreach ($signature in @(
    'public bool TryConsumeFluidLiters(int fluidItemId,',
    'public virtual bool CanAcceptFluidItem(',
    'public virtual bool CanProvideFluidItem(')) {
    $generated += (Read-Member ($base + 'InstallationObject.cs') $signature) + "`n"
}
$generated += "}`npublic partial class InputOutputModule {`n"
foreach ($signature in @(
    'protected IReadOnlyList<InstallationObject> GetConnectedFluidSourceStorages()',
    'private bool EnsureConnectedFluidSourceStorageCache()',
    'private void AddConnectedFluidStorageCacheCandidate(',
    'private void EnqueueConnectedFluidSearchCoordinate(',
    'private bool TryGetConnectedFluidNodeAtCoordinate(',
    'private bool CanFluidSearchLeaveCoordinate(')) {
    $generated += (Read-Member ($base + 'InputOutputModule.cs') $signature) + "`n"
}
$generated += "}`npublic partial class ItemInfoDescription {`n"
$uiFile = 'FactorioProject/Assets/Scripts/HUD/ObjectUI/ItemInfoDescription.cs'
$generated += (Read-Member $uiFile 'private static string FormatFluidStorageText(') + "`n}"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-SprinklerStorage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
