$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Member([string]$file, [string]$signature) {
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
$generated = "using System; using System.Collections.Generic;`npublic partial class InputOutputModule {`n"
foreach ($signature in @(
    'public virtual bool TryGetElectricPowerDemand(', 'protected virtual bool HasOperationalTarget(',
    'public virtual void GetObjectInfoStatus(', 'public static bool IsWaitingObjectInfoStatus(',
    'protected virtual string ResolveObjectInfoStatus(')) {
    $generated += (Member ($base + 'InputOutputModule.cs') $signature) + "`n"
}
$generated += "} public partial class ProductionMachine {`n"
foreach ($signature in @('protected override bool HasOperationalTarget(',
    'protected override string ResolveObjectInfoStatus(')) {
    $generated += (Member ($base + 'ProductionMachine.cs') $signature) + "`n"
}
$generated += "} public static partial class UtilityPole {`n" +
    (Member 'FactorioProject/Assets/Scripts/Map/UtilityPoleRuntime.cs' 'public static bool TryGetElectricPowerInfo(') + "`n}"
$generated += "public partial class ItemInfoDescription {`n" +
    (Member 'FactorioProject/Assets/Scripts/HUD/ObjectUI/ItemInfoDescription.cs' 'private void SetDefaultStatus(') + "`n}"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-IOStatus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
