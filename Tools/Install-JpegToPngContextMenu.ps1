Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$converterScriptPath = Join-Path $scriptDirectory "Convert-JpegToPng.ps1"

if (!(Test-Path -LiteralPath $converterScriptPath)) {
    throw "Converter script was not found: $converterScriptPath"
}

$extensions = @(".jpg", ".jpeg")
$menuKeyName = "ConvertToPng"
$menuText = "Convert To PNG"
$commandValue = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -ShowDialog "%1"' -f $converterScriptPath

function Get-UserChoiceProgId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Extension
    )

    $userChoiceKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$Extension\UserChoice"
    $userChoice = Get-ItemProperty -LiteralPath $userChoiceKeyPath -ErrorAction SilentlyContinue
    if ($null -eq $userChoice) {
        return $null
    }

    return $userChoice.ProgId
}

function Add-TargetKeyPath {
    param(
        [System.Collections.Generic.HashSet[string]]$TargetSet,
        [Parameter(Mandatory = $true)]
        [string]$KeyPath
    )

    if ([string]::IsNullOrWhiteSpace($KeyPath)) {
        return
    }

    [void]$TargetSet.Add($KeyPath)
}

$targetKeyPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
Add-TargetKeyPath -TargetSet $targetKeyPaths -KeyPath "HKCU:\Software\Classes\SystemFileAssociations\image\shell\$menuKeyName"

foreach ($extension in $extensions) {
    Add-TargetKeyPath -TargetSet $targetKeyPaths -KeyPath "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\$menuKeyName"

    $userChoiceProgId = Get-UserChoiceProgId -Extension $extension
    if (![string]::IsNullOrWhiteSpace($userChoiceProgId)) {
        Add-TargetKeyPath -TargetSet $targetKeyPaths -KeyPath "HKCU:\Software\Classes\$userChoiceProgId\shell\$menuKeyName"
    }
}

Add-TargetKeyPath -TargetSet $targetKeyPaths -KeyPath "HKCU:\Software\Classes\jpegfile\shell\$menuKeyName"
Add-TargetKeyPath -TargetSet $targetKeyPaths -KeyPath "HKCU:\Software\Classes\jpgfile\shell\$menuKeyName"

foreach ($baseKeyPath in $targetKeyPaths) {
    $commandKeyPath = Join-Path $baseKeyPath "command"

    New-Item -Path $baseKeyPath -Force | Out-Null
    Set-Item -Path $baseKeyPath -Value $menuText
    New-ItemProperty -Path $baseKeyPath -Name "Icon" -PropertyType String -Value "imageres.dll,-71" -Force | Out-Null
    New-ItemProperty -Path $baseKeyPath -Name "MultiSelectModel" -PropertyType String -Value "Player" -Force | Out-Null

    New-Item -Path $commandKeyPath -Force | Out-Null
    Set-Item -Path $commandKeyPath -Value $commandValue
}

Write-Output "JPEG context menu installed"
