param([Parameter(Mandatory = $true)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $assembly
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-ForestrySerialization-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SerializationChecks.cs') -Destination $probe
$references = ''
foreach ($dll in Get-ChildItem -LiteralPath $assemblyDirectory -Filter '*.dll') {
    $name = [Security.SecurityElement]::Escape($dll.BaseName)
    $path = [Security.SecurityElement]::Escape($dll.FullName)
    $references += '<Reference Include="' + $name + '"><HintPath>' + $path + '</HintPath></Reference>'
}
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>' + $references + '</ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
