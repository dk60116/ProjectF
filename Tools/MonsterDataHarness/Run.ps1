param([switch]$CompileUnity)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
dotnet run --project (Join-Path $PSScriptRoot 'MonsterDataHarness.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Monster Data logic checks failed.' }
dotnet run --project (Join-Path $PSScriptRoot 'RuntimeChecks.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Monster runtime combat checks failed.' }

$checks = 0
function Require([bool]$condition, [string]$message) {
    $script:checks++
    if (-not $condition) { throw $message }
}
function ReadGuid([string]$path) {
    $match = [regex]::Match((Get-Content -LiteralPath $path -Raw), '(?m)^guid: ([a-f0-9]{32})\r?$')
    Require $match.Success "Missing GUID: $path"
    return $match.Groups[1].Value
}

$assets = Join-Path $root 'FactorioProject/Assets'
$monsterView = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Monster/MonsterInstance.cs') -Raw
Require ($monsterView.Contains('sealed class MonsterInstance : IDisposable')) 'Monster focus identity has no actor MonoBehaviour'
Require ($monsterView.Contains('definition.TryGetRuntimeData') -and $monsterView.Contains('Icon = variant.icon')) 'Monster view uses resolved stats and authored portrait'
Require ($monsterView.Contains('renderer.bounds.IntersectRay') -and $monsterView.Contains('renderer.enabled')) 'Monster focus uses visible view bounds'
Require ($monsterView.Contains('new WorldHealthBarView(parent, "Monster Health Bar")') -and $monsterView.Contains('healthBar?.Dispose()')) 'Monster overhead HP reuses the Animal bar and releases it with the actor'
$animalHealthBar = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Animal/AnimalWorldHealthBar.cs') -Raw
Require ($animalHealthBar.Contains('new WorldHealthBarView(parent, "Animal Health Bar")') -and -not $animalHealthBar.Contains('typeof(Canvas)')) 'Animal delegates bar geometry and visibility to the same shared view without duplicate Canvas construction'
$monsterWorld = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Monster/MonsterWorld.cs') -Raw
$monsterWorldView = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Monster/MonsterWorldView.cs') -Raw
Require ($monsterWorld.Contains('presentation.NotifyAttackAnimationStarted(actor.Monster)') -and $monsterWorldView.Contains('world?.RefreshHealthBars(transform, GetCamera())') -and $monsterWorldView.Contains('DefaultExecutionOrder(900)')) 'One late presentation loop follows the camera and receives actual ECS attack notifications'
Require ($monsterView.Contains('new MonsterCollisionBody(root)') -and $monsterView.Contains('collisionBody.SetEnabled(!disposed && CurrentHealth > 0f)') -and $monsterView.Contains('collisionBody.SetEnabled(false)')) 'Spawned views bind authored collision proxies and disable them on death/disposal'
$collisionBody = $monsterView.Substring($monsterView.IndexOf('internal sealed class MonsterCollisionBody'))
Require ($collisionBody.Contains('root.GetComponent<CapsuleCollider>()') -and $collisionBody.Contains('root.GetComponent<Rigidbody>()') -and -not $collisionBody.Contains('AddComponent') -and -not $collisionBody.Contains('FitCapsule')) 'Monster physics comes from prefabs, not runtime component creation or resizing'
Require (-not $collisionBody.Contains('Physics.SyncTransforms') -and -not $collisionBody.Contains('MonoBehaviour')) 'Collision proxies add no per-actor scripts or per-tick global physics sync'
$physicsSettings = Get-Content -LiteralPath (Join-Path $root 'FactorioProject/ProjectSettings/DynamicsManager.asset') -Raw
$collisionMatrix = [Convert]::FromHexString([regex]::Match($physicsSettings, '(?m)^  m_LayerCollisionMatrix: ([0-9a-fA-F]+)').Groups[1].Value)
Require (([BitConverter]::ToUInt32($collisionMatrix, 3 * 4) -band (1 -shl 8)) -ne 0 -and ([BitConverter]::ToUInt32($collisionMatrix, 8 * 4) -band (1 -shl 3)) -ne 0) 'Player and Animal layers permit physical collision in both directions'
$spawner = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Monster/MonsterSpawner.cs') -Raw
Require ($spawner.Contains('new MonsterInstance(spawned, definition, variantId)')) 'Test spawn registers its selection identity'
Require ($spawner.Contains('MonsterWorld.Register(monster, definition, terrain)')) 'Spawn enters the runtime combat world'
$editorSpawner = Get-Content -LiteralPath (Join-Path $assets 'Editor/Monsters/MonsterEditorSpawner.cs') -Raw
Require ($editorSpawner.Contains('MonsterWorld.Current?.Dispose()') -and -not $editorSpawner.Contains('PlayableGraph')) 'Editor harness delegates animation ownership and cleanup to runtime'
$brush = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Monster/MonsterSpawnBrush.cs') -Raw
Require ($brush.StartsWith('#if UNITY_EDITOR') -and $brush.Contains('Input.GetKeyDown(KeyCode.Escape)') -and $brush.Contains('Input.GetMouseButtonDown(0)')) 'Brush is Editor-only and handles Escape plus left clicks'
Require ($brush.Contains('MonsterSpawner.TrySpawnAt(') -and $brush.Contains('cancelledFrame == Time.frameCount')) 'Brush spawns at the selected point and consumes its cancellation frame'
$monsterEditor = Get-Content -LiteralPath (Join-Path $assets 'Editor/Monsters/MonsterDataEditorWindow.cs') -Raw
Require ($monsterEditor.Contains('DrawPaintButton(draft, selectedVariantId)') -and $monsterEditor.Contains('GUILayout.Toggle(active,') -and $monsterEditor.Contains('MonsterSpawnBrush.Begin(draft.Source, variantId)')) 'MonsterData has a selected-variant brush toggle'
Require ([regex]::IsMatch($monsterEditor, 'private void OnDisable\(\)\s*\{\s*MonsterSpawnBrush.Stop\(\)') -and $editorSpawner.Contains('MonsterSpawnBrush.Stop()')) 'Window closure and Play Mode teardown stop brush input'
$world = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/Monster/MonsterWorld.cs') -Raw
Require ($world.Contains('actor.Monster.ApplySimulationPose(actor.Position, actor.Rotation)') -and -not $world.Contains('Root.transform.SetPositionAndRotation')) 'The shared ECS tick writes each collision and presentation pose through one owner'
Require ($world.Contains('bodyCells.TryGetValue') -and $world.Contains('ConstrainBodyMove(actor, next - position)') -and $world.Contains('ResolveBodyOverlap(actor,') -and $world.Contains('bodyBucketPool')) 'ECS movement sweeps nearby bodies, slides, resolves initial overlap and reuses spatial buckets'
Require ((Get-Content -LiteralPath (Join-Path $assets 'Scripts/Character/Player/PlayerController.ConveyorCollision.cs') -Raw).Contains('monsterWorld.TrySweepBody(')) 'The actual combined player movement sweep includes authoritative monster bodies'
Require ($world.Contains('IMapObjectUpdateTick') -and -not $world.Contains('MonoBehaviour')) 'Monster simulation has a shared fixed-tick owner'
Require ($world.Contains('AnimalGridPathfinder.FindPath') -and $world.Contains('TryRefreshCombatTarget')) 'Monster chase uses existing grid navigation and refreshed target identities'
$manager = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Manager/GameManager.cs') -Raw
Require ($manager.Contains('ProjectF.Monsters.MonsterSpawnBrush.Tick()')) 'GameManager drives editor brush input independently of player state'
Require ($manager.Contains('private bool cloakingMode;') -and $manager.Contains('public bool CloakingMode => cloakingMode')) 'GameManager owns the Inspector-visible cloaking toggle'
Require ($manager.Contains('SetCloakingMode(value)') -and $manager.Contains('BuildCloakingModeExtraTokens(GameManager.Instance)') -and $manager.Contains('extraTokens: BuildCloakingModeExtraTokens(gameManager)')) 'EditorTool command, acknowledgement and status share cloaking state'
$editorTool = Get-Content -LiteralPath (Join-Path $root 'Tools/ItemGiveTool/Program.cs') -Raw
Require ($editorTool.Contains('debugTogglePanel.Controls.Add(cloakingModeCheckBox)') -and $editorTool.Contains('cloakingModeCheckBox.CheckedChanged +=')) 'EditorTool exposes a connected cloaking checkbox'
Require ($editorTool.Contains('ApplyRuntimeCheckBoxState(cloakingModeCheckBox, cloakingMode)') -and $editorTool.Contains('cloakingModeCheckBox.Enabled = !busy')) 'Cloaking checkbox follows runtime status and command busy state'
$combat = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Map/TerrainGenerator.Combat.cs') -Raw
Require ($combat.Contains('TryGetInstallationStorageKey(sequence, definition.id') -and -not $combat.Contains('TryGetInstallationAnchorAtCoordinate')) 'Damage resolves placement identity, not overlapping I/O coordinate owners'
Require ($combat.Contains('resourceStateStore.RemoveInstallation(target.StorageKey)') -and $combat.Contains('ReleaseInstallationObject(live')) 'Lethal damage uses canonical data and scene teardown'
$placementStore = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Map/BlockStateStore.cs') -Raw
Require ($placementStore.Contains('bool existingPlacement = savedInstallationStorageKeysByPlacement.TryGetValue(') -and $placementStore.Contains('if (!existingPlacement) RemoveWreckageOverlapping(storedState);')) 'Committed new placements clear overlapping wreckage without affecting routine state captures'
$commitStart = $placementStore.IndexOf('private bool StoreInstallationState(')
$commitEnd = $placementStore.IndexOf('internal void MarkMapMarkersChanged()', $commitStart)
$commitSource = $placementStore.Substring($commitStart, $commitEnd - $commitStart)
Require ($commitSource.IndexOf('RemoveWreckageOverlapping(storedState)') -gt $commitSource.IndexOf('savedInstallationStates[storageKey] = storedState;') -and $commitSource.IndexOf('RemoveWreckageOverlapping(storedState)') -lt $commitSource.LastIndexOf('return true;')) 'Wreck cleanup follows successful canonical installation storage, never preview validation'
Require ($combat.Contains('definition.excludeFromMonsterTargets') -and -not $combat.Contains('target.AllowsAnimalTraversal')) 'Attack exclusion is authored separately from traversal'
$item = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/ItemDefinition.cs') -Raw
Require ($item.Contains('public float maxHealth = 100f') -and $item.Contains('excludeFromMonsterTargets')) 'ItemDefinition owns map-object HP and monster-target policy'
$itemEditor = Get-Content -LiteralPath (Join-Path $assets 'Editor/ItemDataEditorWindow.cs') -Raw
Require ($itemEditor.Contains('GetSelectedDefinitionProperty(serializedObject, "maxHealth")') -and $itemEditor.Contains('GetSelectedDefinitionProperty(serializedObject, "excludeFromMonsterTargets")')) 'ItemData exposes both health authoring fields'
Require ($item.Contains('public int monsterAttackPriority = 1') -and $item.Contains('[Range(0, 3)') -and $item.Contains('Mathf.Clamp(monsterAttackPriority, 0, 3)')) 'Structure attack priority defaults to one and supports zero through three'
Require ($itemEditor.Contains('Mathf.Clamp(entry.monsterAttackPriority, 0, 3)')) 'ItemData JSON import preserves priority three'
Require ($itemEditor.Contains('GetSelectedDefinitionProperty(serializedObject, "monsterAttackPriority")') -and $itemEditor.Contains('GetMultiSelectedDefinitionProperty(serializedObject, "monsterAttackPriority")')) 'ItemData exposes priority in single and multi edit'
Require ($itemEditor.Contains('monsterAttackPriority = definition.MonsterAttackPriority') -and $itemEditor.Contains('if (entry.monsterAttackPriority >= 0)')) 'Priority JSON roundtrip preserves missing legacy values'
Require ([regex]::IsMatch($itemEditor, 'if \(AllSelectedDefinitionsAreInstallations\(\)\)\s*\{\s*DrawMultiPropertyField\(excludeFromMonsterTargetsProperty[^;]+;\s*DrawMultiPropertyField\(monsterAttackPriorityProperty')) 'Multi-selection shows attack settings only for installation-only selections'
Require ([regex]::IsMatch($itemEditor, 'if \(definition\.mapObject is InstallationObject\)\s*\{\s*DrawMultiPropertyField\(excludeFromMonsterTargetsProperty[^;]+;\s*DrawMultiPropertyField\(monsterAttackPriorityProperty')) 'Single selection hides installation attack settings on ordinary items and resources'
$mapHealthView = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Simulation/Presentation/MapObjectHealthPresentation.cs') -Raw
Require ($mapHealthView.Contains('store.DamagedInstallations') -and $mapHealthView.Contains('state.combatHealth < maximum')) 'Floating map-object bars only visit damaged placements'
Require (-not $mapHealthView.Contains('MonoBehaviour :') -and $mapHealthView.Contains('TryRaycastWreckage')) 'Wreckage has presentation-only ownership and explicit removal selection'
$health = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Character/Player/Player.Combat.cs') -Raw
Require ($health.Contains('public bool Revive()') -and $health.Contains('state.hasHealth')) 'Player has explicit manual revive and saved death restoration'
$hud = Get-Content -LiteralPath (Join-Path $assets 'Scripts/HUD/PlayerHUD.Combat.cs') -Raw
Require ($hud.Contains('SetActive(player.IsDead)') -and $hud.Contains('onClick.AddListener(RevivePlayer)')) 'Dead player has a manual revive HUD action'
$player = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Character/Player/PlayerController.cs') -Raw
$knife = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Character/Player/PlayerController.Knife.cs') -Raw
Require ($player.Contains('TickKnifeAttack();') -and $player.Contains('TryGetKnifeApproachDirection(out Vector3 knifeApproachDirection)')) 'Player Update drives the shared knife approach and attack timing'
Require ($player.Contains('return RequestKnifeAttack(animal);') -and $knife.Contains('RequestMonsterKnifeInteraction(MonsterInstance monster) => RequestKnifeAttack(monster)')) 'Animal and monster attacks share one knife state machine'
Require ((Get-Content -LiteralPath (Join-Path $assets 'Scripts/Character/Player/Player.cs') -Raw).Contains('playerController.IsKnifeInteractionActive')) 'Both knife targets use the existing equipped knife visual'
Require ([regex]::IsMatch($player, 'public void ClearInteractionPointSnapForLoad\(\)\s*\{\s*CancelKnifeInteraction\(\);')) 'Load and player death cancel pending knife swings immediately'
$playerHud = Get-Content -LiteralPath (Join-Path $assets 'Scripts/HUD/PlayerHUD.cs') -Raw
Require ($playerHud.Contains('currentInteractionMonster = selectedMonster;') -and $playerHud.Contains('RequestMonsterKnifeInteraction(currentInteractionMonster)') -and $playerHud.Contains('|| currentInteractionMonster != null')) 'Selected monsters use the existing knife button and Space input'
Require ($playerHud.Contains('TryGetKnifeFocusTarget(out object knifeFocusTarget)') -and $playerHud.Contains('clickedObjectInfoTarget = knifeFocusTarget;')) 'Knife approach and swing keep either target pinned in InfoPanel'
Require ($player.Contains('MonsterSpawnBrush.BlocksGameplayInput')) 'Brush claims normal player interaction input'
$placement = Get-Content -LiteralPath (Join-Path $assets 'Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs') -Raw
Require ($placement.Contains('MonsterSpawnBrush.BlocksGameplayInput') -and (Get-Content -LiteralPath (Join-Path $assets 'Scripts/HUD/PlayerHUD.cs') -Raw).Contains('MonsterSpawnBrush.BlocksGameplayInput')) 'Brush clicks cannot install buildings or invoke primary HUD pointer actions'
Require ($player.Contains('monsterDistance <= closestDistance') -and $player.Contains('monsterDistance <= closestAnimalDistance') -and $player.Contains('monsterDistance <= closestPortableObjectDistance')) 'Monster selection respects nearer focus targets'
$panel = Get-Content -LiteralPath (Join-Path $assets 'Scripts/HUD/ObjectUI/ObjectInfoPanel.cs') -Raw
Require ($panel.Contains('infoLine.ShowMonster(monster)') -and $panel.Contains('SetCustomDisplay(monster.Icon, monster.DisplayName')) 'InfoPanel binds monster details and portrait'
Require (-not $panel.Contains('TryGetMapObjectHealth')) 'Map-object HP is not added to InfoPanel'
$info = Get-Content -LiteralPath (Join-Path $assets 'Scripts/HUD/ObjectUI/ItemInfoDescription.cs') -Raw
Require ($info.Contains('liveGaugeMonster = monster') -and $info.Contains('RefreshMonsterHealthGauge()')) 'Monster InfoPanel binds a live HP gauge'
Require (-not $monsterView.Contains('InfoText')) 'Unused all-stats monster description is removed'
$scriptGuid = ReadGuid (Join-Path $assets 'Scripts/Object/Monster/MonsterDefinition.cs.meta')
$speciesIds = [Collections.Generic.HashSet[int]]::new()
$species = @('Bat', 'Ghost', 'Rabbit', 'Slime')
$modelScales = @{ Bat = 10; Ghost = 20; Rabbit = 20; Slime = 20 }
$meshExtents = @{
    Bat = @(0.30703323, 0.71625828, 0.20000998)
    Ghost = @(0.16111932, 0.1809302, 0.16919938)
    Rabbit = @(0.27250372, 0.20392912, 0.27692194)
    Slime = @(0.24838318, 0.25835475, 0.22145892)
}
$boneOffsetMax = @{ Bat = 0.23888728; Ghost = 0.134638008; Rabbit = 0.132654588; Slime = 0.4 }
$colliderShapes = @{
    Bat = @(0.170008, 1.432517, -0.247761, 0.031125)
    Ghost = @(0.136951, 0.361860, -0.052677, 0)
    Rabbit = @(0.231628, 0.463256, -0.155106, 0)
    Slime = @(0.188240, 0.516709, -0.122798, 0)
}
$culture = [Globalization.CultureInfo]::InvariantCulture
$totalVariants = 0
foreach ($name in $species) {
    $definition = Join-Path $assets "Monsters/$name/Monster_$name.asset"
    $text = Get-Content -LiteralPath $definition -Raw
    Require ($text.Contains("guid: $scriptGuid")) "$name definition script reference"
    $id = [regex]::Match($text, '(?m)^  id: (\d+)\r?$')
    Require ($id.Success -and $speciesIds.Add([int]$id.Groups[1].Value)) "$name unique species ID"
    Require ($text -match "(?m)^  monsterName: $name\r?$") "$name species name"
    Require ($text -match '(?m)^  nextVariantId: 4\r?$') "$name next variant ID"
    Require ($text -match '(?m)^  commonDrops: \[\]\r?$') "$name initial drops deliberately empty"
    Require ($text -match '(?m)^    DetectionRange: 100\r?$') "$name common detection radius is one hundred"
    $variantBlocks = [regex]::Matches($text, '(?ms)^  - id: (?<id>\d+)\r?\n(?<body>.*?)(?=^  - id:|^  nextVariantId:|\z)')
    Require ($variantBlocks.Count -eq 4) "$name has four initial variants"
    $variantIds = [Collections.Generic.HashSet[int]]::new()
    foreach ($variant in $variantBlocks) {
        $totalVariants++
        Require ($variantIds.Add([int]$variant.Groups['id'].Value)) "$name unique variant ID"
        $body = $variant.Groups['body'].Value
        Require ($body -match '(?m)^    overrideMask: 0\r?$') "$name initial stat inheritance"
        Require ($body -match '(?m)^    overrideDrops: 0\r?$') "$name initial drop inheritance"
        Require ($body -match '(?m)^      DetectionRange: 100\r?$') "$name variant detection defaults match the common radius"
        $color = [regex]::Match($body, '(?m)^    variantName: (\w+)\r?$').Groups[1].Value
        if ($name -eq 'Bat' -or $name -eq 'Slime') {
            $icon = Join-Path $assets "Monsters/Portraits/$($name)_$color.png"
            $iconGuid = ReadGuid ($icon + '.meta')
            Require ($body -match "(?m)^    icon: \{fileID: 21300000, guid: $iconGuid, type: 3\}\r?$") "$name/$color portrait Sprite reference"
            $iconMeta = Get-Content -LiteralPath ($icon + '.meta') -Raw
            foreach ($setting in @('textureType: 8', 'spriteMode: 1', 'enableMipMap: 0', 'alphaIsTransparency: 1', 'nPOTScale: 0')) {
                Require ($iconMeta.Contains($setting)) "$name/$color portrait $setting"
            }
            Require ($iconMeta -match '(?m)^    maxTextureSize: 512\r?$') "$name/$color portrait GPU size cap"
            Require ($iconMeta -match '(?m)^    textureCompression: 0\r?$') "$name/$color portrait clean alpha import"
            $png = [IO.File]::ReadAllBytes($icon)
            Require ($png.Length -gt 33 -and $png[0] -eq 137 -and [Text.Encoding]::ASCII.GetString($png, 1, 3) -eq 'PNG') "$name/$color portrait PNG"
            $width = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($png, 16))
            $height = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($png, 20))
            Require ($width -eq $height -and $width -ge 512 -and $png[25] -eq 6) "$name/$color square RGBA portrait"
        }
        $prefab = Join-Path $assets "Level 1 Monster Pack/Prefabs/$name/$($name)_$color.prefab"
        $guid = ReadGuid ($prefab + '.meta')
        $reference = [regex]::Match($body, '(?m)^    prefab: \{fileID: (\d+), guid: ([a-f0-9]{32}), type: 3\}\r?$')
        Require ($reference.Success -and $reference.Groups[2].Value -eq $guid) "$name/$color prefab GUID"
        $prefabText = Get-Content -LiteralPath $prefab -Raw
        $fileId = $reference.Groups[1].Value
        $rootBlock = [regex]::Match($prefabText, "(?ms)^--- !u!1 &$fileId\r?\n(?<body>.*?)(?=^---|\z)")
        Require ($rootBlock.Success -and $rootBlock.Groups['body'].Value -match "(?m)^  m_Name: $($name)_$color\r?$") "$name/$color prefab root file ID"
        Require ($rootBlock.Groups['body'].Value -match '(?m)^  m_Layer: 8\r?$') "$name/$color root uses Animal collision layer"
        $documentIds = @([regex]::Matches($prefabText, '(?m)^--- !u!\d+ &(\d+)\r?$') | ForEach-Object { $_.Groups[1].Value })
        Require (@($documentIds | Group-Object | Where-Object Count -gt 1).Count -eq 0) "$name/$color prefab document IDs are unique"
        $capsules = [regex]::Matches($prefabText, '(?ms)^--- !u!136 &(?<id>\d+)\r?\nCapsuleCollider:\r?\n(?<body>.*?)(?=^---|\z)')
        $bodies = [regex]::Matches($prefabText, '(?ms)^--- !u!54 &(?<id>\d+)\r?\nRigidbody:\r?\n(?<body>.*?)(?=^---|\z)')
        Require ($capsules.Count -eq 1 -and $bodies.Count -eq 1) "$name/$color has exactly one authored capsule and Rigidbody"
        foreach ($component in @($capsules[0], $bodies[0])) {
            $componentId = $component.Groups['id'].Value
            Require ($component.Groups['body'].Value -match "(?m)^  m_GameObject: \{fileID: $fileId\}\r?$" -and $rootBlock.Groups['body'].Value.Contains("  - component: {fileID: $componentId}")) "$name/$color physics is linked to the root in both directions"
        }
        $capsuleText = $capsules[0].Groups['body'].Value
        $bodyText = $bodies[0].Groups['body'].Value
        foreach ($setting in @('m_IsTrigger: 0', 'm_Enabled: 1', 'm_Direction: 1')) {
            Require ($capsuleText.Contains($setting)) "$name/$color solid capsule $setting"
        }
        foreach ($setting in @('m_UseGravity: 0', 'm_IsKinematic: 1', 'm_Interpolate: 0', 'm_CollisionDetection: 3')) {
            Require ($bodyText.Contains($setting)) "$name/$color ECS body $setting"
        }
        $radius = [double]::Parse([regex]::Match($capsuleText, '(?m)^  m_Radius: ([^\r\n]+)').Groups[1].Value, $culture)
        $height = [double]::Parse([regex]::Match($capsuleText, '(?m)^  m_Height: ([^\r\n]+)').Groups[1].Value, $culture)
        $center = [regex]::Match($capsuleText, '(?m)^  m_Center: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\r?$')
        Require ($radius -gt 0 -and $height + 1e-6 -ge $radius * 2 -and $center.Success) "$name/$color capsule geometry is valid"
        $shape = $colliderShapes[$name]
        Require ([Math]::Abs($radius - $shape[0]) -lt 1e-6 -and [Math]::Abs($height - $shape[1]) -lt 1e-6) "$name/$color body-sized radius and height"
        $centerY = [double]::Parse($center.Groups[2].Value, $culture)
        Require ([Math]::Abs($centerY - $height * 0.5) -lt 1e-7) "$name/$color capsule bottom must touch root-local ground y=0"
        $expectedCenter = @($shape[2], ($height * 0.5), $shape[3])
        for ($axis = 0; $axis -lt 3; $axis++) {
            $actual = [double]::Parse($center.Groups[$axis + 1].Value, $culture)
            Require ([Math]::Abs($actual - $expectedCenter[$axis]) -lt 1e-6) "$name/$color grounded capsule center axis $axis"
        }
        $scales = [regex]::Matches($prefabText, '(?m)^  m_LocalScale: (.*)\r?$')
        Require ($scales.Count -gt 0) "$name/$color transforms exist"
        foreach ($scale in $scales) {
            Require ($scale.Groups[1].Value.Trim() -eq '{x: 1, y: 1, z: 1}') "$name/$color keeps unit transform scale"
        }
        $extent = [regex]::Match($prefabText, '(?m)^    m_Extent: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\r?$')
        Require $extent.Success "$name/$color skinned renderer bounds exist"
        for ($axis = 0; $axis -lt 3; $axis++) {
            $actual = [double]::Parse($extent.Groups[$axis + 1].Value, $culture)
            Require ([Math]::Abs($actual - $meshExtents[$name][$axis]) -lt 1e-7) "$name/$color 20x renderer extent axis $axis"
        }
        $maxOffset = 0.0
        foreach ($position in [regex]::Matches($prefabText, '(?m)^  m_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\r?$')) {
            for ($axis = 1; $axis -le 3; $axis++) {
                $maxOffset = [Math]::Max($maxOffset, [Math]::Abs([double]::Parse($position.Groups[$axis].Value, $culture)))
            }
        }
        Require ([Math]::Abs($maxOffset - $boneOffsetMax[$name]) -lt 1e-7) "$name/$color 20x rest skeleton offsets"
    }
    $model = Get-ChildItem -LiteralPath (Join-Path $assets 'Level 1 Monster Pack/Models') -Filter "$($name)_*.fbx.meta" | Select-Object -First 1
    $modelGuid = ReadGuid $model.FullName
    $modelText = Get-Content -LiteralPath $model.FullName -Raw
    $importScale = [regex]::Match($modelText, '(?m)^    globalScale: ([^\r\n]+)\r?$')
    Require ($importScale.Success -and [double]::Parse($importScale.Groups[1].Value, $culture) -eq $modelScales[$name]) "$name 20x imported mesh/animation scale"
    foreach ($mapping in @(@('idle', 'idle'), @('move', 'move'), @('attack', 'attack'), @('hit', 'damage'), @('death', 'die'))) {
        $field = $mapping[0]
        $suffix = $mapping[1]
        if ($name -eq 'Slime' -and $field -eq 'hit') {
            Require ($text -match '(?m)^    hit: \{fileID: 0\}\r?$') 'Slime hit clip remains unassigned'
            continue
        }
        $reference = [regex]::Match($text, "(?m)^    ${field}: \{fileID: (\d+), guid: ([a-f0-9]{32}), type: 3\}\r?$")
        Require ($reference.Success -and $reference.Groups[2].Value -eq $modelGuid) "$name/$field FBX GUID"
        $fileId = $reference.Groups[1].Value
        Require ($modelText -match "(?m)^    ${fileId}: $($name.ToLowerInvariant())_$suffix\r?$") "$name/$field clip local file ID"
    }
}
Require ($totalVariants -eq 16) 'Sixteen initial prefab bindings'
Write-Host "PASS: $checks Monster Data asset reference checks"

if ($CompileUnity) {
    $target = Join-Path $PSScriptRoot 'Compile.targets'
    dotnet build (Join-Path $root 'FactorioProject/Assembly-CSharp-Editor.csproj') --no-restore -v:q "-p:CustomAfterMicrosoftCommonTargets=$target" '-clp:ErrorsOnly;Summary'
    if ($LASTEXITCODE -ne 0) { throw 'Unity runtime/editor C# compilation failed.' }
}
