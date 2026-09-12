$ErrorActionPreference = 'Stop'
# Runtime coverage moved with the production simulation to the entity/world harness.
& (Join-Path $PSScriptRoot '../RobotArmEcsHarness/Run.ps1')
exit $LASTEXITCODE
