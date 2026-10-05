$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../ForestryEcsHarness/Run.ps1')
exit $LASTEXITCODE
