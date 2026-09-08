$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-TrainSpacing-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$train = [IO.File]::ReadAllText((Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Train.cs'))
$constant = [regex]::Match($train, 'public const float ConnectionCenterDistance = [^;]+;')
if (!$constant.Success) { throw 'Missing production spacing constant' }
[IO.File]::WriteAllText((Join-Path $probe 'Spacing.cs'), ('public partial class Train { ' + $constant.Value + ' }'))
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/TrainPlacementSpacing.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
