param([Parameter(Mandatory = $true)][string]$CompiledAssemblyDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $repo 'FactorioProject'
$compiled = [IO.Path]::GetFullPath($CompiledAssemblyDirectory)
if (!(Test-Path -LiteralPath (Join-Path $compiled 'Assembly-CSharp.dll'))) { throw 'Compile runtime code first' }
[xml]$runtimeProject = Get-Content -LiteralPath (Join-Path $project 'Assembly-CSharp.csproj')
$references = @($compiled, (Join-Path $project 'Library/ScriptAssemblies'))
foreach ($hint in $runtimeProject.Project.ItemGroup.Reference.HintPath) {
    if (!$hint) { continue }
    $path = if ([IO.Path]::IsPathRooted($hint)) { $hint } else { Join-Path $project $hint }
    if (Test-Path -LiteralPath $path) { $references += [IO.Path]::GetDirectoryName($path) }
}
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BenchmarkTerrain-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$cecil = Get-ChildItem -Path (Join-Path $project 'Library/PackageCache/com.unity.nuget.mono-cecil*/Mono.Cecil.dll') | Select-Object -First 1
if (!$cecil) { throw 'Unity Mono.Cecil dependency is missing' }
[IO.File]::WriteAllLines((Join-Path $probe 'references.txt'), @($references | Select-Object -Unique))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
$cecilPath = [Security.SecurityElement]::Escape($cecil.FullName)
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><Reference Include=`"Mono.Cecil`"><HintPath>$cecilPath</HintPath></Reference></ItemGroup></Project>")
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj') -- $compiled (Join-Path $probe 'references.txt')
exit $LASTEXITCODE
