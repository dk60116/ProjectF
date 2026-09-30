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
        Get-Content -LiteralPath $log | Select-String 'error |오류 '
        Write-Output "Compile log: $log"
        exit $LASTEXITCODE
    }
    Write-Output "$name compiled successfully. Log: $log"
}
