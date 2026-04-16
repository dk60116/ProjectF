Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$extensions = @(".jpg", ".jpeg")
$menuKeyName = "ConvertToPng"

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
    if (Test-Path -LiteralPath $baseKeyPath) {
        Remove-Item -LiteralPath $baseKeyPath -Recurse -Force
    }
}

Write-Output "JPEG context menu removed"
