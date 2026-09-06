$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Read-Member([string]$file, [string]$signature, [int]$occurrence = 0) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file))
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    for ($i = 0; $i -lt $occurrence -and $start -ge 0; $i++) {
        $start = $source.IndexOf($signature, $start + $signature.Length, [StringComparison]::Ordinal)
    }
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $end = $source.IndexOf('{', $start) + 1
    $depth = 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $signature" }
    $source.Substring($start, $end - $start)
}
$generated = "using System;`nusing System.IO;`nusing System.Collections.Generic;`npublic partial class AnimalAIController {`n"
$controller = 'FactorioProject/Assets/Scripts/Object/Animal/AnimalAIController.cs'
foreach ($signature in @('private bool TryTickFeeding(', 'private bool IsWithinDroppedFoodReach(', 'private bool CanReachDroppedFood(', 'private void GetNavigationArea(', 'private void GetExtendedNavigationArea(', 'private bool IsOutsideRoamingArea(')) {
    $generated += (Read-Member $controller $signature) + "`n"
}
$generated += (Read-Member $controller 'private bool MoveTowardTarget(').Replace('private bool MoveTowardTarget(', 'private bool MoveTowardTargetLive(') + "`n"
foreach ($signature in @('private MovementAvailability ResolveMovementDirection(', 'private MovementAvailability FindAvoidanceDirection(', 'private bool TryFindAvoidanceDirectionOnSide(', 'private bool CanUseAvoidanceDirection(', 'private void CommitAvoidance(', 'private void ClearAvoidanceCommitment(', 'private MovementAvailability ProbeMovementDirection(', 'private bool HandleBlockedMovement(', 'private bool IsNavigationProgressStalled(', 'private void AbandonCurrentNavigationTarget(', 'private void ResetToIdleBehavior(', 'private void ResetNavigation(', 'private bool EnsureNavigationForTarget(', 'private bool CanNavigateDirectly(', 'private bool TryBuildNavigationPath(', 'private bool StoreNavigationPath(', 'private void PrepareDirectNavigation(', 'private void GetCurrentNavigationTarget(', 'private bool CanOccupyTerrain(', 'private void SuppressHerdReturnRetry(')) {
    $generated += (Read-Member $controller $signature) + "`n"
}
$generated += (Read-Member $controller 'private bool TryBuildNavigationPath(' 1) + "`n"
$generated += (Read-Member $controller 'private void ApplyAnimation(') + "`n"
$generated += (Read-Member $controller 'private bool FaceDroppedFood(') + "`n"
foreach ($signature in @('private bool TryChooseTarget(', 'private bool TryBuildReachableFallbackPath(', 'private bool TryBeginHerdAreaReturn(', 'private bool TryPrepareNearestHerdReturnTarget(', 'private uint NextRandomUInt(', 'private float Next01(')) {
    $generated += (Read-Member $controller $signature) + "`n"
}
$generated += (Read-Member $controller 'private void ReleaseFoodConsumption(') + "`n"
$generated += (Read-Member $controller 'private bool CanNavigateDirectly(' 1) + "`n}`npublic partial class Animal {`n"
foreach ($signature in @('internal bool ConsumeDroppedFood(', 'private void AddFoodGrowth(', 'public void SetAge(', 'private void RestoreNeedsState(', 'internal void TickNeeds(', 'internal bool CompleteDefecation(', 'internal bool IsDefecationDue')) {
    $generated += (Read-Member 'FactorioProject/Assets/Scripts/Object/Animal/Animal.cs' $signature) + "`n"
}
$generated += "}`npublic partial class TerrainGenerator {`n"
foreach ($signature in @('public bool TryFindNearestDroppedAnimalFood(', 'public bool TryConsumeDroppedAnimalFood(', 'private static bool IsAnimalFoodItemId(', 'private bool IsAnimalFoodBeingConsumed(', 'public void ReleaseAnimalFoodConsumption(')) {
    $generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/TerrainGenerator.AnimalNeeds.cs' $signature) + "`n"
}
$generated += "}`n"
$generated += "public partial class Block {`n"
foreach ($signature in @('private static PortableObject GetTopPortableObject(', 'private static void CleanupPortableStack(')) {
    $generated += (Read-Member 'FactorioProject/Assets/Scripts/Map/Block.cs' $signature) + "`n"
}
$generated += "}`n"
$generated += (Read-Member 'FactorioProject/Assets/Scripts/Manager/SaveGameData.cs' 'public sealed class AnimalSaveEntry') + "`npublic static partial class GrowthSaveProbe {`n"
foreach ($signature in @('private static void WriteAnimalEntry(', 'private static AnimalSaveEntry ReadAnimalEntry(', 'private static void WriteVector3(', 'private static Vector3 ReadVector3(', 'private static void WriteQuaternion(', 'private static Quaternion ReadQuaternion(', 'private static void WriteList<T>(', 'private static List<T> ReadList<T>(')) {
    $generated += (Read-Member 'FactorioProject/Assets/Scripts/Manager/SaveGameBinarySerializer.cs' $signature) + "`n"
}
$generated += "}`n"
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('ProjectF-AnimalFeeding-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
Set-Content -LiteralPath (Join-Path $probeDir 'Production.cs') -Value $generated
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Object/Animal/AnimalGridPathfinder.cs') -Destination (Join-Path $probeDir 'AnimalGridPathfinder.cs')
Copy-Item -LiteralPath (Join-Path $repo 'FactorioProject/Assets/Scripts/Map/Block.AnimalFood.cs') -Destination (Join-Path $probeDir 'Block.AnimalFood.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GrowthChecks.cs') -Destination (Join-Path $probeDir 'GrowthChecks.cs')
$checksPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Checks.cs'))
$movementPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'MovementChecks.cs'))
Set-Content -LiteralPath (Join-Path $probeDir 'Probe.csproj') -Value ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0414;0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="' + $checksPath + '" /><Compile Include="' + $movementPath + '" /></ItemGroup></Project>')
dotnet run --configuration Release --project (Join-Path $probeDir 'Probe.csproj')
exit $LASTEXITCODE
