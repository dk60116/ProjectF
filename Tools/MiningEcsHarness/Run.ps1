$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-MiningEcs-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probe | Out-Null
$files = @('Simulation/Core/ProductionProcess.cs', 'Simulation/Core/MiningProcess.cs',
    'Simulation/Core/SimulationTickContracts.cs', 'Map/MiningMachineInstance.cs', 'Map/MiningItemOutput.cs',
    'Map/IDataElectricConsumer.cs')
foreach ($relative in $files) { Copy-Item -LiteralPath (Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)) -Destination $probe }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BoundaryStubs.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GaugeChecks.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OwnershipChecks.cs') -Destination $probe
function Member([string]$relative, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $relative)))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++
    }
    $source.Substring($start, $end - $start)
}
$io = 'Object/MapObj/InstallationObject/InputOutputModule.cs'
$generated = "using System; using System.IO; using System.Collections.Generic; using UnityEngine; public partial class InputOutputModule {`n"
foreach ($signature in @('public struct PersistentInputItemAreaState', 'public sealed class RefineryOutputState', 'public sealed class PersistentState')) {
    $generated += (Member $io $signature) + "`n"
}
$generated += "}`npublic static class MiningSaveProbe { public const int MaxSerializedListCount = 1000000;`n"
foreach ($signature in @('private static void WriteInputOutputState(', 'private static InputOutputModule.PersistentState ReadInputOutputState(',
    'private static void WriteVector2Int(', 'private static Vector2Int ReadVector2Int(', 'private static void WriteVector2IntList(',
    'private static List<Vector2Int> ReadVector2IntList(', 'private static void WriteInputItemAreaList(',
    'private static List<InputOutputModule.PersistentInputItemAreaState> ReadInputItemAreaList(',
    'private static void WriteIntList(', 'private static List<int> ReadIntList(', 'private static void WriteLongList(',
    'private static List<long> ReadLongList(', 'private static void WriteList<T>(', 'private static List<T> ReadList<T>(')) {
    $generated += (Member 'Manager/SaveGameBinarySerializer.cs' $signature) + "`n"
}
$generated += @'
public static byte[] Encode(InputOutputModule.PersistentState state) {
    using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
    WriteInputOutputState(writer, state); writer.Flush(); return memory.ToArray();
}
public static InputOutputModule.PersistentState Decode(byte[] data, int version) {
    using var memory = new MemoryStream(data); using var reader = new BinaryReader(memory);
    var state = ReadInputOutputState(reader, version);
    if (memory.Position != memory.Length) throw new Exception("Unexpected trailing save bytes"); return state;
}
}
'@
[IO.File]::WriteAllText((Join-Path $probe 'SaveProduction.cs'), $generated)
$store = "using System; using System.Collections.Generic; using UnityEngine; public partial class BlockStateStore {`n"
foreach ($signature in @('public bool RegisterDataOnlyInstallation(', 'internal bool RegisterDataOnlyInstallationSharedState(',
    'private bool StoreInstallationState(', 'private void StoreInstallationState(', 'public void UpdateInstallationState(',
    'public bool TryGetInstallationState(Vector2Int storageKey,',
    'internal bool TryGetInstallationStateReadOnly(', 'public List<InstallationSaveState> GetInstallationStatesSnapshot()')) {
    $store += (Member 'Map/BlockStateStore.cs' $signature) + "`n"
}
$store += "}`n"
[IO.File]::WriteAllText((Join-Path $probe 'StoreProduction.cs'), $store)
$power = "using System; using System.Collections.Generic; using UnityEngine; public static partial class PowerDemandProbe {`n"
$power += (Member 'Object/MapObj/InstallationObject/UtilityPole.RobotArms.cs' 'internal static void InvalidateDataConsumerDemand(') + "`n}"
[IO.File]::WriteAllText((Join-Path $probe 'PowerProduction.cs'), $power)
$view = 'Simulation/Presentation/MiningWorldView.cs'
$viewSource = [IO.File]::ReadAllText((Join-Path $repo ('FactorioProject/Assets/Scripts/' + $view)))
$fieldStart = $viewSource.IndexOf('    private MiningWorld world;')
$fieldEnd = $viewSource.IndexOf('    public int VisibleCount')
$gaugeProduction = "using System.Collections.Generic; using UnityEngine; using ProjectF.MapObjects; using ProjectF.Rendering; using Camera = CameraBoundary;`npublic sealed partial class MiningWorldView {`n" + $viewSource.Substring($fieldStart, $fieldEnd - $fieldStart)
$gaugeProduction += "public int VisibleCount { get; private set; }`n"
foreach ($signature in @('internal void Unbind(', 'private void ReleaseCollider(', 'private void OnDisable()', 'private void LateUpdate()',
    'private void UpdateWorkGauge(', 'private void ReleaseHiddenWorkGauges()', 'private void ReleaseWorkGauge(',
    'private static void ReleaseGauge(', 'private void ReleaseAllWorkGauges()')) {
    $gaugeProduction += (Member $view $signature) + "`n"
}
$gaugeProduction += "}`npublic partial class MiningWorld {`n"
$gaugeProduction += (Member 'Map/MiningWorld.cs' 'internal bool ShouldShowLinkedUi(') + "`n}`n"
$gaugeProduction += (Member 'Map/AreaMarker.cs' 'internal readonly struct AreaMarkerVisibilityContext')
[IO.File]::WriteAllText((Join-Path $probe 'GaugeProduction.cs'), $gaugeProduction)
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll'
[IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="UnityEngine.CoreModule"><HintPath>' + $unity + '</HintPath></Reference></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probe 'Probe.csproj')
exit $LASTEXITCODE
