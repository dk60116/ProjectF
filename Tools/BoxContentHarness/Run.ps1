param([switch]$Baseline)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Box/BoxObject.cs'
$sourceText = if ($Baseline) { (git -C $repo show "HEAD:$sourcePath") -join "`n" } else { [IO.File]::ReadAllText((Join-Path $repo $sourcePath)) }
function Member([string]$signature) {
    $start = $sourceText.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing member: $signature" }
    $end = $sourceText.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0) {
        if ($sourceText[$end] -eq '{') { $depth++ }
        if ($sourceText[$end] -eq '}') { $depth-- }
        $end++
    }
    return $sourceText.Substring($start, $end - $start)
}
$source = "using System.Collections.Generic; public partial class BoxObject {`n"
foreach ($signature in @('private bool TryGetContentBlock(', 'public bool CanPutContainedObjects(', 'public bool TryPutOneContainedObjectInstant(')) {
    $source += (Member $signature) + "`n"
}
$source += '}'
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-BoxContent-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
[IO.File]::WriteAllText((Join-Path $probe 'Production.cs'), $source)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
