$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assets = Join-Path $root 'FactorioProject/Assets'
$checks = 0
function Require([bool]$value, [string]$message) {
    $script:checks++
    if (-not $value) { throw $message }
}

$mapObject = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/MapObj/MapObject.cs') -Raw
Require ($mapObject -match 'NearOne\s*=\s*1\b' -and $mapObject -match 'None\s*=\s*2\b') 'Serialized focus enum values changed; update asset validation.'
Require ($mapObject.Contains('public bool AllowsFocus => multiFocusMode != MultiFocusMode.None;')) 'Scene selection must honor authored focus mode.'
$world = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Map/BuildingWorld.cs') -Raw
Require ($world.Contains('public bool AllowsFocus => Prototype.AllowsFocus;') -and $world.Contains('public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;')) 'Data-only building selection must honor its prototype focus mode.'

$script = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/MapObj/InstallationObject/Building/Wall.cs.meta') -Raw
$scriptGuid = [regex]::Match($script, '(?m)^guid: ([a-f0-9]{32})').Groups[1].Value
Require ($scriptGuid.Length -eq 32) 'Wall script GUID missing.'
foreach ($kind in @('Brick Wall', 'Wooden Fence')) {
    $directory = Join-Path $assets "MapObject/Wall/$kind"
    foreach ($suffix in @('', '_Corner', '_TriCorner', '_Cross')) {
        $path = Join-Path $directory "$kind$suffix.prefab"
        $prefab = Get-Content -LiteralPath $path -Raw
        $components = [regex]::Matches($prefab, '(?ms)^--- !u!114 &[^\r\n]+\r?\n(?<body>.*?)(?=^---|\z)')
        $walls = @($components | Where-Object { $_.Groups['body'].Value.Contains("guid: $scriptGuid,") })
        Require ($walls.Count -eq 1) "$kind$suffix has exactly one Wall component."
        $body = $walls[0].Groups['body'].Value
        Require ($body -match '(?m)^  multiFocusMode: 1\r?$') "$kind$suffix must permit individual focus."
        $radius = [regex]::Match($body, '(?m)^  installationFocusRadius: ([^\r\n]+)').Groups[1].Value
        Require ($radius -and [double]::Parse($radius, [Globalization.CultureInfo]::InvariantCulture) -gt 0) "$kind$suffix must have a positive nearby-focus radius."
        Require ($body -match '(?m)^  itemDefinition: \{fileID: 11400000, guid: [a-f0-9]{32}, type: 2\}\r?$') "$kind$suffix retains its item identity."
    }
}
$door = Get-Content -LiteralPath (Join-Path $assets 'MapObject/Wall/Wooden Fence Door/Wooden Fence Door.prefab') -Raw
Require ($door -match '(?m)^  multiFocusMode: 1\r?$') 'Door focus remains enabled.'
Write-Host "PASS: $checks wall focus asset/source contract checks"

$hud = Get-Content -LiteralPath (Join-Path $assets 'Scripts/HUD/PlayerHUD.cs') -Raw
$method = [regex]::Match($hud, '(?s)    private bool IsObjectInfoTargetAvailable\(.*?(?=    private PlayerController ResolvePlayerController\()')
if (-not $method.Success) { throw 'PlayerHUD availability method not found.' }
$fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'HudAvailabilityFixture.cs') -Raw
Add-Type -TypeDefinition $fixture.Replace('// PRODUCTION_METHOD', $method.Value)
$runtimeChecks = [WallFocusHarness.HudFixture]::Run()
Write-Host "PASS: $runtimeChecks production HUD availability checks"

$boxTriangles = [regex]::Match($world, '(?s)    private static readonly int\[\] BoxTriangles\s*=\s*\{.*?\};')
$appendBox = [regex]::Match($world, '(?s)    private static void AppendBox\(.*?(?=\r?\n\})')
if (-not $boxTriangles.Success -or -not $appendBox.Success) { throw 'BuildingWorld collision geometry methods not found.' }
$collisionFixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CollisionFixture.cs') -Raw
Add-Type -TypeDefinition $collisionFixture.Replace('// PRODUCTION_TRIANGLES', $boxTriangles.Value).Replace('// PRODUCTION_APPEND_BOX', $appendBox.Value)
$collisionChecks = [WallCollisionHarness.GeometryChecks]::Run()
Write-Host "PASS: $collisionChecks production building collision geometry checks"

$view = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Simulation/Presentation/BuildingWorldView.cs') -Raw
Require ($view.Contains('root.layer = LayerMask.NameToLayer("Object");')) 'Shared building collision host must use the installation layer.'
Require ($view.IndexOf('root.layer =') -lt $view.IndexOf('root.AddComponent<MeshCollider>()')) 'Building collision layer must be assigned before creating the collider.'
$tags = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/ProjectSettings/TagManager.asset') -Raw
$layerBlock = [regex]::Match($tags, '(?s)  layers:\r?\n(?<body>.*?)(?=  m_SortingLayers:)').Groups['body'].Value
$layers = @($layerBlock -split '\r?\n' | Where-Object { $_ -match '^  -' } | ForEach-Object { $_.Substring(4).Trim() })
$playerLayer = [array]::IndexOf($layers, 'Player')
$objectLayer = [array]::IndexOf($layers, 'Object')
Require ($playerLayer -ge 0 -and $objectLayer -ge 0) 'Player and Object layers must exist.'
$physics = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/ProjectSettings/DynamicsManager.asset') -Raw
$matrix = [regex]::Match($physics, '(?m)^  m_LayerCollisionMatrix: ([a-fA-F0-9]+)').Groups[1].Value
function ReadLayerMask([int]$layer) {
    $bytes = [byte[]]::new(4)
    for ($i = 0; $i -lt 4; $i++) { $bytes[$i] = [Convert]::ToByte($matrix.Substring($layer * 8 + $i * 2, 2), 16) }
    return [BitConverter]::ToUInt32($bytes, 0)
}
Require (((ReadLayerMask $playerLayer) -band (1 -shl $objectLayer)) -ne 0) 'Player movement casts must include the building Object layer.'
Require (((ReadLayerMask $objectLayer) -band (1 -shl $playerLayer)) -ne 0) 'Object layer must collide with Player symmetrically.'
Write-Host 'PASS: 5 building collision layer checks'

function ReadWorldMember([string]$signature) {
    $start = $world.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing BuildingWorld member: $signature" }
    $end = $world.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $world.Length) {
        if ($world[$end] -eq '{') { $depth++ }
        if ($world[$end] -eq '}') { $depth-- }
        $end++
    }
    return $world.Substring($start, $end - $start)
}
$lifecycleFixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BuildingLifecycleFixture.cs') -Raw
$members = @{
    ATTACH = 'public void AttachView()'
    VIEW_DESTROYED = 'internal void OnViewDestroyed('
    SUPPRESSION = 'internal void SetPlacementPresentationSuppressed('
    SCALE = 'internal void SetPlacementPresentationScale('
    RENDER = 'internal void Render('
    GEOMETRY = 'internal void AppendCollisionGeometry('
}
foreach ($key in $members.Keys) { $lifecycleFixture = $lifecycleFixture.Replace("// PRODUCTION_$key", (ReadWorldMember $members[$key])) }
Add-Type -TypeDefinition $lifecycleFixture
$lifecycleChecks = [BuildingLifecycleHarness.BuildingWorld]::Run()
Write-Host "PASS: $lifecycleChecks production building collision lifecycle checks"
