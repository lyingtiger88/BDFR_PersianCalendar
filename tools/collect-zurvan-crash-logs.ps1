# Collect diagnostic text for Zurvan Desktop without copying notes or account secrets.
# Example: powershell -ExecutionPolicy Bypass -File .\tools\collect-zurvan-crash-logs.ps1
param([switch]$IncludeMiniDump)

$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'BDFR\PersianCalendar'
$folder = Join-Path $base 'Diagnostics'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stage = Join-Path $env:TEMP "Zurvan-Diagnostics-$stamp"
$archive = Join-Path ([Environment]::GetFolderPath('Desktop')) "Zurvan-Diagnostics-$stamp.zip"
New-Item -ItemType Directory -Force $stage | Out-Null
try {
    foreach ($name in @('startup.log','last-startup-phase.txt')) {
        $source = Join-Path $base $name
        if (Test-Path $source) { Copy-Item $source $stage -Force }
    }
    if (Test-Path $folder) {
        $target = Join-Path $stage 'Diagnostics'
        New-Item -ItemType Directory -Force $target | Out-Null
        Get-ChildItem $folder -File | Where-Object {
            $_.Name -match '^(breadcrumbs\.log(\.\d+)?|session-active\.txt|last-action\.txt|crash-context\.txt)$'
        } | Copy-Item -Destination $target -Force
        if ($IncludeMiniDump) {
            $dumps = Join-Path $folder 'CrashDumps'
            if (Test-Path $dumps) {
                $last = Get-ChildItem $dumps -Filter '*.dmp' -File |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if ($last) { Copy-Item $last.FullName $target -Force }
            }
        }
    }
    try {
        $since = (Get-Date).AddDays(-2)
        Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$since} -MaxEvents 400 -ErrorAction Stop |
            Where-Object { $_.ProviderName -in @('Application Error','.NET Runtime','Windows Error Reporting','Application Hang') -and
                $_.Message -match 'BDFR\.PersianCalendar\.Desktop' } |
            Select-Object -First 20 TimeCreated,ProviderName,Id,LevelDisplayName,Message |
            Format-List | Out-File (Join-Path $stage 'windows-crashes.txt') -Encoding utf8
    } catch { }
    if (Test-Path $archive) { Remove-Item $archive -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "Created: $archive"
    Write-Host 'Please review log paths and crash details for sensitive information before sharing.'
    Write-Host 'No calendar.db, settings.json, notes, credentials, or full dumps are included by default.'
}
finally { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
