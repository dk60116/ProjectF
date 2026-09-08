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
    $source.Substring($start, $end - $start)
}

$rail = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Railload.cs'
$shader = 'FactorioProject/Assets/Shaders/ToonCharacter.shader'
$railSource = [IO.File]::ReadAllText((Join-Path $repo $rail))
$shaderSource = [IO.File]::ReadAllText((Join-Path $repo $shader))
if (!$railSource.Contains('SetMaterialFloat(material, "_UseWorldUpLighting", 1f);')) {
    throw 'Rail runtime materials must enable world-up lighting'
}
if (!$shaderSource.Contains('_UseWorldUpLighting("Use World Up Lighting", Float) = 0')) {
    throw 'The shared toon shader must keep world-up lighting disabled by default'
}
if (!$shaderSource.Contains('saturate(_UseWorldUpLighting)')) {
    throw 'The toon lighting normal must use the world-up material option'
}

$generated = "using System.Collections.Generic; using UnityEngine;`npublic partial class Railload {`n"
$generated += Read-Member $rail 'private static Vector3 ResolveFlatSide('
$generated += Read-Member $rail 'private static void AddSleeperBox('
$generated += Read-Member $rail 'private static void AddQuad('
$generated += Read-Member $rail 'private static void AddQuadVertices('
$generated += "`npublic static void BuildSleeper(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 tangent) => AddSleeperBox(vertices, triangles, center, tangent, 1.2f, 0.28f, 0.12f);`n}"

$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-RailLighting-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $generated)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
