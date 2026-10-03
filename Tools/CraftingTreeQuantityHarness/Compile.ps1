$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $repo 'FactorioProject'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-CraftingCompile-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
foreach ($name in @('Assembly-CSharp', 'Assembly-CSharp-Editor')) {
    [xml]$original = Get-Content -LiteralPath (Join-Path $project "$name.csproj")
    [xml]$generated = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9.0</LangVersion><AllowUnsafeBlocks>true</AllowUnsafeBlocks><NoWarn>1701;1702</NoWarn></PropertyGroup><ItemGroup /></Project>'
    $group = $generated.SelectSingleNode('/Project/ItemGroup')
    foreach ($pair in @(@('AssemblyName', $name), @('DefineConstants', $original.Project.PropertyGroup.DefineConstants))) {
        $node = $generated.CreateElement($pair[0]); $node.InnerText = $pair[1]
        $generated.Project.PropertyGroup.AppendChild($node) | Out-Null
    }
    $sources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($include in $original.Project.ItemGroup.Compile.Include) {
        if (!$include) { continue }
        $path = Join-Path $project $include
        if (Test-Path -LiteralPath $path) { $sources.Add($path) | Out-Null }
    }
    if ($name -eq 'Assembly-CSharp') {
        $sources.Add((Join-Path $project 'Assets/Scripts/Manager/CraftingTreeQuantity.cs')) | Out-Null
        foreach ($relative in @('Diagnostics/BenchmarkLayout.cs', 'Diagnostics/BenchmarkCommand.cs', 'Diagnostics/BenchmarkRuntime.cs',
            'Manager/RuntimeItemGiveReceiver.Benchmark.cs', 'Map/TerrainGenerator.Benchmark.cs', 'Map/TerrainGenerator.Benchmark.Spawning.cs',
            'Object/MapObj/InstallationObject/InputOutputModule.Benchmark.cs', 'Object/MapObj/InstallationObject/LoggingMachine.Benchmark.cs')) {
            $sources.Add((Join-Path $project ('Assets/Scripts/' + $relative))) | Out-Null
        }
        foreach ($relative in @('Rendering/InstallationMaterialVariants.cs', 'Rendering/InstallationBatchRenderer.cs',
            'Rendering/ProductionEffectTemplate.cs', 'Rendering/SpriteMeshCache.cs', 'Rendering/InstallationRigidAnimationTemplate.cs', 'Map/IDataElectricConsumer.cs', 'Map/MiningWorld.cs',
            'Map/IProductionFacilityInfo.cs', 'Map/ProductionFacilityInstance.Info.cs', 'Map/ProductionFacilityInstance.cs', 'Map/ProductionRenderTemplate.cs', 'Map/ProductionWorld.cs', 'Map/ProductionWorld.Fluid.cs',
            'Map/UtilityPoleRuntime.cs', 'Map/UtilityPoleRuntime.Identity.cs', 'Map/UtilityPoleRuntime.Consumers.cs',
            'Map/UtilityPoleWorld.cs', 'Map/UtilityPoleRenderTemplate.cs', 'Map/TerrainGenerator.UtilityPoles.cs',
            'Simulation/Presentation/UtilityPoleWorldView.cs', 'Rendering/UtilityPoleWireRenderer.cs',
            'Map/IDataItemProducer.cs', 'Map/TerrainGenerator.Production.cs', 'Simulation/Presentation/ProductionWorldView.cs',
            'Map/MiningMachineInstance.cs', 'Map/MiningRenderTemplate.cs', 'Map/MiningItemOutput.cs',
            'Map/TerrainGenerator.Mining.cs', 'Simulation/Core/MiningProcess.cs', 'Simulation/Presentation/MiningWorldView.cs',
            'Map/Block.DeferredOutputs.cs', 'Map/PortableItemRenderer.DeferredOutputs.cs', 'Simulation/Core/OutputStackBatch.cs',
            'Map/TerrainGenerator.ConveyorJobs.Publication.cs', 'Rendering/BeltItemVisualPath.cs')) {
            $sources.Add((Join-Path $project ('Assets/Scripts/' + $relative))) | Out-Null
        }
    }
    if ($name -eq 'Assembly-CSharp-Editor') {
        $sources.Add((Join-Path $project 'Assets/Editor/InstallationArchetypeBuildPreparation.cs')) | Out-Null
    }
    foreach ($path in $sources) {
        $node = $generated.CreateElement('Compile'); $node.SetAttribute('Include', $path)
        $group.AppendChild($node) | Out-Null
    }
    $references = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($hint in $original.Project.ItemGroup.Reference.HintPath) {
        if (!$hint) { continue }
        $path = if ([IO.Path]::IsPathRooted($hint)) { $hint } else { Join-Path $project $hint }
        $filename = [IO.Path]::GetFileName($path)
        if ((Test-Path -LiteralPath $path) -and $filename -notmatch '^(System(\.|\.dll)|mscorlib\.dll|netstandard\.dll)') {
            $references.Add($path) | Out-Null
        }
    }
    foreach ($reference in $original.Project.ItemGroup.ProjectReference.Include) {
        if (!$reference) { continue }
        $assembly = [IO.Path]::GetFileNameWithoutExtension($reference) + '.dll'
        $path = if ($name -eq 'Assembly-CSharp-Editor' -and $assembly -eq 'Assembly-CSharp.dll') {
            Join-Path $probe "bin/Debug/netstandard2.1/$assembly"
        } else { Join-Path $project "Library/ScriptAssemblies/$assembly" }
        $references.Add($path) | Out-Null
    }
    foreach ($path in $references) {
        $node = $generated.CreateElement('Reference'); $node.SetAttribute('Include', [IO.Path]::GetFileNameWithoutExtension($path))
        $hint = $generated.CreateElement('HintPath'); $hint.InnerText = $path
        $node.AppendChild($hint) | Out-Null
        $group.AppendChild($node) | Out-Null
    }
    $path = Join-Path $probe "$name.csproj"
    $generated.Save($path)
    $log = Join-Path $probe "$name.log"
    dotnet build $path --nologo --verbosity quiet *> $log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $log | Select-String -SimpleMatch 'error '
        Write-Output "Compile log: $log"
        exit $LASTEXITCODE
    }
    Write-Output "$name compiled successfully. Log: $log"
}
