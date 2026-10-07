param(
    [switch]$Disable,
    [string]$DumpFolder = "$env:LOCALAPPDATA\BDFR\PersianCalendar\CrashDumps",
    [int]$DumpCount = 10
)

$ErrorActionPreference = "Stop"

$exeName = "BDFR.PersianCalendar.Desktop.exe"
$keyPath = "HKCU:\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\$exeName"

if ($Disable) {
    if (Test-Path $keyPath) {
        Remove-Item $keyPath -Recurse -Force
    }

    Write-Host "Anahita WER LocalDumps configuration removed for $exeName."
    exit 0
}

New-Item -ItemType Directory -Path $DumpFolder -Force | Out-Null
New-Item -Path $keyPath -Force | Out-Null

New-ItemProperty -Path $keyPath -Name DumpFolder -PropertyType ExpandString -Value $DumpFolder -Force | Out-Null
New-ItemProperty -Path $keyPath -Name DumpType -PropertyType DWord -Value 2 -Force | Out-Null
New-ItemProperty -Path $keyPath -Name DumpCount -PropertyType DWord -Value ([Math]::Max(1, $DumpCount)) -Force | Out-Null

Write-Host "Anahita full crash dumps enabled (current user, no admin required)."
Write-Host "Executable: $exeName"
Write-Host "Dump folder: $DumpFolder"
Write-Host "Disable with: .\Enable-AnahitaCrashDumps.ps1 -Disable"
