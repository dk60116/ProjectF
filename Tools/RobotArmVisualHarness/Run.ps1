$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$world = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/RobotArmWorld.cs'))
$entity = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArmInstance.cs'))
$template = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/RobotArmRenderTemplate.cs'))
if ([regex]::Matches($world, 'new GameObject\(').Count -ne 1 -or $world -match 'Instantiate\(') { throw 'Installed arms must allocate only the common host.' }
if ($entity -match '\b(?:Transform|Animator|GameObject|MonoBehaviour|PortableObject)\s+\w+\s*(?:;|=)' -or $entity -match 'Instantiate\(') { throw 'Entity contains per-arm scene state.' }
if ($template -match 'Instantiate\(|AddComponent|\.SetActive\(|\.localRotation\s*=') { throw 'Template rendering must not create or animate scene nodes.' }
if ($world -notmatch 'IsAnyLayerVisible\([\s\S]*?culling.Intersects\([\s\S]*?continue;[\s\S]*?arm.Template.Append') { throw 'Culling must precede building the rig matrices.' }
if ($template -notmatch 'EvaluateChain' -or $template -notmatch 'ItemPresentationPosition') { throw 'Endpoint/held item presentation path is missing.' }
& (Join-Path $repo 'Tools/RobotArmAnimationBake/Run.ps1') -Check
Write-Output 'PASS: installed host allocation, data-only entities, non-mutating rig rendering, cull-before-build, held item presentation and authored curve validation.'
