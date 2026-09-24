$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-AreaMarker-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Map/AreaMarker.cs'))
$placementSource = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InstallationPlacementController.cs'))
if (-not $placementSource.Contains('PipeInputItemMarkerRectGridBlockTypes')) { throw 'Pipe input item marker type filter missing' }
if (-not $placementSource.Contains('AddPipeInputItemAreaMarkerRequests(')) { throw 'Pipe input item icon marker builder missing' }
if (-not $placementSource.Contains('GetArrowMarkerRotationZ(markerWorldPosition, referenceWorldPosition)')) { throw 'Pipe input arrow must point from the input area toward the machine' }
$inputBuilderStart = $placementSource.IndexOf('private void AddPipeInputItemAreaMarkerRequests(', [StringComparison]::Ordinal)
$outputBuilderStart = $placementSource.IndexOf('private void AddPipeOutputAreaMarkerRequests(', [StringComparison]::Ordinal)
$outputBuilderEnd = $placementSource.IndexOf('private static Vector3 ResolveNearestAreaMarkerReferenceWorldPosition(', $outputBuilderStart, [StringComparison]::Ordinal)
if ($inputBuilderStart -lt 0 -or $outputBuilderStart -le $inputBuilderStart) { throw 'Pipe input marker builder boundary missing' }
if ($outputBuilderStart -lt 0 -or $outputBuilderEnd -le $outputBuilderStart) { throw 'Pipe output marker builder boundary missing' }
$inputBuilder = $placementSource.Substring($inputBuilderStart, $outputBuilderStart - $inputBuilderStart)
$outputBuilder = $placementSource.Substring($outputBuilderStart, $outputBuilderEnd - $outputBuilderStart)
if (-not $inputBuilder.Contains('ResolvePipePassMarkerIcon(footprintSource)')) { throw 'Pipe input marker must resolve the configured input fluid' }
if ($inputBuilder.Contains('ResolvePipeOutputMarkerIcon(footprintSource)')) { throw 'Pipe input marker must not resolve its icon from configured outputs' }
if (-not $outputBuilder.Contains('ResolvePipeOutputMarkerIcon(footprintSource)')) { throw 'Pipe output marker must resolve the configured output fluid' }
if ($outputBuilder.Contains('ResolvePipePassMarkerIcon(footprintSource)')) { throw 'Pipe output marker must not resolve its icon from the input recipe' }
if (-not $placementSource.Contains('TryAppendConfiguredOutputItemIds(areaMarkerOutputItemIdsScratch)')) { throw 'Pipe output marker must include virtual outputs such as oil drilling machines' }
$boundary = $source.IndexOf('internal sealed class InstallationPlacementAreaRegistry', [StringComparison]::Ordinal)
if ($boundary -lt 0) { throw 'Area registry boundary missing' }
[IO.File]::WriteAllText((Join-Path $probe 'AreaMarker.cs'), $source.Substring(0, $boundary))
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/AreaMarkerRenderer.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityStubs.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
