$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scripts = Join-Path $repo 'FactorioProject/Assets/Scripts'
function Member([string]$source, [string]$signature) {
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
    return $source.Substring($start, $end - $start)
}
$vehicle = Get-Content -LiteralPath (Join-Path $scripts 'Object/MapObj/InstallationObject/Vehicle/Vehicle.cs') -Raw
$generated = "using UnityEngine; using UnityEngine.UI; public partial class Vehicle {`n"
foreach ($signature in @('public bool TryDockPlayer(', 'public bool TryDockPlayerAtPoint(',
    'public virtual bool CanPlayerDock(', 'protected virtual bool PreparePlayerForDock(',
    'private bool DockPlayerToPoint(', 'public bool TryGetPlayerPoint(', 'public bool TryGetPlayerPointIndex(',
    'public bool TryGetNearestPlayerPoint(', 'private static float GetPlanarDistanceSqr(',
    'public virtual void NotifyPlayerDismounted(')) {
    $generated += (Member $vehicle $signature) + "`n"
}
$controller = Get-Content -LiteralPath (Join-Path $scripts 'Character/Player/PlayerController.cs') -Raw
$generated += "} public partial class PlayerController {`n"
foreach ($signature in @('public bool CanDismountFromVehicle()', 'private bool TryResolveInteractionPointExitPosition(',
    'private bool TryResolveClearVehicleDismountPosition(',
    'private static bool IsClearVehicleDismountBlock(', 'private static bool CoordinatePrecedes(')) {
    $generated += (Member $controller $signature) + "`n"
}
$generated += "}`n"
$hud = Get-Content -LiteralPath (Join-Path $scripts 'HUD/PlayerHUD.cs') -Raw
$generated += "public partial class PlayerHUD {`n"
foreach ($signature in @('private bool TryActivateEquippedMapObjectInteraction(',
    'private bool TryActivateNearestAutomaticMapObjectInteraction(', 'private bool TryActivateMapObjectInteraction(',
    'private bool TryResolveMapObjectInteraction(', 'private static bool CanDisplayClickedMapObjectInteraction(',
    'private void ActivateMapObjectInteraction(',
    'private void SetActiveInteractionButton(', 'private static void HideInteractionButton(',
    'private static Sprite ResolveInteractionIcon(IMapObjectTarget',
    'private static Sprite ResolveInteractionIcon(int itemId, int preferredIconIndex, bool',
    "private static Sprite ResolveInteractionIcon(`r`n        ItemDefinition")) {
    $generated += (Member $hud $signature) + "`n"
}
$generated += "public void TickEquippedInteraction() { var currentPlayer = GameManager.Instance.Player; var playerController = currentPlayer.GetComponent<PlayerController>();`n"
$generated += (Member $hud 'if (currentPlayer.HasRangedEquipment)') + "`n}`n"
$generated += "public void TickMountedInteraction() { var playerController = GameManager.Instance.Player.GetComponent<PlayerController>(); var mountedVehicle = playerController.MountedVehicle;`n"
$generated += (Member $hud 'if (mountedVehicle != null)') + "`n} }`n"
$button = Get-Content -LiteralPath (Join-Path $scripts 'HUD/Button/InteractionButton.cs') -Raw
$generated += "public sealed partial class InteractionButton {`n"
$generated += (Member $button 'public void SetVisible(') + "`n}`n"
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ShipDriving-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
$files = @((Join-Path $PSScriptRoot 'Checks.cs'), (Join-Path $PSScriptRoot 'Boundaries.cs'), (Join-Path $PSScriptRoot 'HudChecks.cs'),
    (Join-Path $probe 'Production.cs'), (Join-Path $scripts 'Object/MapObj/InstallationObject/Vehicle/Ship.cs'),
    (Join-Path $scripts 'Object/MapObj/InstallationObject/Vehicle/Raft.cs'),
    (Join-Path $scripts 'Simulation/Core/VehicleMotionState.cs'))
$compile = ($files | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }) -join "`n"
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + $compile + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Ship driving/mount/dismount checks failed.' }
$node = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
& $node (Join-Path $PSScriptRoot 'AssetChecks.cjs') $repo
if ($LASTEXITCODE -ne 0) { throw 'Raft asset bindings failed.' }
